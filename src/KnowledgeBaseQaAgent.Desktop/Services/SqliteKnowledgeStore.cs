using System.Text.Json;
using KnowledgeBaseQaAgent.Desktop.Models;
using Microsoft.Data.Sqlite;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class SqliteKnowledgeStore
{
    private readonly AppPaths _paths;
    private bool _sqliteVecAvailable;

    public SqliteKnowledgeStore(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        _sqliteVecAvailable = TryLoadSqliteVec(connection);

        var schema = connection.CreateCommand();
        schema.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS documents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                path TEXT NOT NULL,
                title TEXT NOT NULL,
                content_hash TEXT NOT NULL UNIQUE,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS chunks (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                document_id INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                text TEXT NOT NULL,
                source_path TEXT NOT NULL,
                source_label TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                embedding_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON chunks(document_id);
            CREATE TABLE IF NOT EXISTS messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at TEXT NOT NULL,
                provider_id TEXT NOT NULL,
                model TEXT NOT NULL,
                citation_chunk_ids TEXT
            );
            """;
        await schema.ExecuteNonQueryAsync(cancellationToken);

        if (_sqliteVecAvailable)
        {
            await TryCreateVectorTableAsync(connection, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SourceDocument>> GetDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id, path, title, content_hash, created_at FROM documents ORDER BY created_at DESC;";
        var documents = new List<SourceDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new SourceDocument(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))));
        }

        return documents;
    }

    public async Task<bool> HasDocumentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM documents WHERE content_hash = $hash LIMIT 1;";
        command.Parameters.AddWithValue("$hash", contentHash);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task DeleteDocumentAsync(long documentId, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var chunkIds = await GetChunkIdsAsync(connection, documentId, cancellationToken);

        var deleteDocument = connection.CreateCommand();
        deleteDocument.Transaction = (SqliteTransaction)transaction;
        deleteDocument.CommandText = "DELETE FROM documents WHERE id = $id;";
        deleteDocument.Parameters.AddWithValue("$id", documentId);
        await deleteDocument.ExecuteNonQueryAsync(cancellationToken);

        await TryDeleteVectorsAsync(connection, chunkIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearKnowledgeBaseAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var clearChunks = connection.CreateCommand();
        clearChunks.Transaction = (SqliteTransaction)transaction;
        clearChunks.CommandText = "DELETE FROM chunks;";
        await clearChunks.ExecuteNonQueryAsync(cancellationToken);

        var clearDocuments = connection.CreateCommand();
        clearDocuments.Transaction = (SqliteTransaction)transaction;
        clearDocuments.CommandText = "DELETE FROM documents;";
        await clearDocuments.ExecuteNonQueryAsync(cancellationToken);

        await TryClearVectorsAsync(connection, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<long> AddDocumentAsync(string path, string title, string contentHash, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents(path, title, content_hash, created_at)
            VALUES($path, $title, $hash, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$hash", contentHash);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task AddChunkAsync(
        long documentId,
        int ordinal,
        string text,
        string sourcePath,
        string sourceLabel,
        string contentHash,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO chunks(document_id, ordinal, text, source_path, source_label, content_hash, embedding_json, created_at)
            VALUES($documentId, $ordinal, $text, $sourcePath, $sourceLabel, $contentHash, $embedding, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        command.Parameters.AddWithValue("$ordinal", ordinal);
        command.Parameters.AddWithValue("$text", text);
        command.Parameters.AddWithValue("$sourcePath", sourcePath);
        command.Parameters.AddWithValue("$sourceLabel", sourceLabel);
        command.Parameters.AddWithValue("$contentHash", contentHash);
        command.Parameters.AddWithValue("$embedding", JsonSerializer.Serialize(embedding));
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        var chunkId = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);

        if (_sqliteVecAvailable)
        {
            await TryInsertVectorAsync(connection, chunkId, embedding, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(float[] queryEmbedding, int topK, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.document_id, c.ordinal, c.text, c.source_path, c.source_label, c.content_hash, c.created_at, c.embedding_json
            FROM chunks c;
            """;

        var results = new List<SearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var embedding = JsonSerializer.Deserialize<float[]>(reader.GetString(8)) ?? [];
            var chunk = new KnowledgeChunk(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                DateTimeOffset.Parse(reader.GetString(7)),
                embedding);
            results.Add(new SearchResult(chunk, Cosine(queryEmbedding, embedding)));
        }

        return results
            .OrderByDescending(result => result.Score)
            .Take(Math.Max(1, topK))
            .ToArray();
    }

    public async Task AddMessageAsync(
        string role,
        string content,
        string providerId,
        string model,
        string? citationChunkIds,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO messages(role, content, created_at, provider_id, model, citation_chunk_ids)
            VALUES($role, $content, $createdAt, $providerId, $model, $citationChunkIds);
            """;
        command.Parameters.AddWithValue("$role", role);
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue("$providerId", providerId);
        command.Parameters.AddWithValue("$model", model);
        command.Parameters.AddWithValue("$citationChunkIds", (object?)citationChunkIds ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentMessagesAsync(int take = 30, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, role, content, created_at, provider_id, model, citation_chunk_ids
            FROM messages
            ORDER BY id DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$take", take);

        var messages = new List<ChatMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new ChatMessage(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        messages.Reverse();
        return messages;
    }

    public async Task ClearMessagesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM messages;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_paths.DatabasePath}");
        connection.Open();
        connection.CreateFunction("cosine_dot", (double x) => x);
        return connection;
    }

    private static bool TryLoadSqliteVec(SqliteConnection connection)
    {
        try
        {
            connection.EnableExtensions(true);
            connection.LoadVector();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task TryCreateVectorTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = "CREATE VIRTUAL TABLE IF NOT EXISTS chunk_vectors USING vec0(embedding float[384]);";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // Managed cosine search remains the compatibility fallback.
        }
    }

    private static async Task<IReadOnlyList<long>> GetChunkIdsAsync(SqliteConnection connection, long documentId, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM chunks WHERE document_id = $documentId;";
        command.Parameters.AddWithValue("$documentId", documentId);

        var ids = new List<long>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt64(0));
        }

        return ids;
    }

    private static async Task TryDeleteVectorsAsync(SqliteConnection connection, IReadOnlyList<long> chunkIds, CancellationToken cancellationToken)
    {
        if (chunkIds.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var chunkId in chunkIds)
            {
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM chunk_vectors WHERE rowid = $id;";
                command.Parameters.AddWithValue("$id", chunkId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch
        {
            // The managed JSON chunk store is authoritative; vec cleanup is best-effort.
        }
    }

    private static async Task TryClearVectorsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM chunk_vectors;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // The managed JSON chunk store is authoritative; vec cleanup is best-effort.
        }
    }

    private static async Task TryInsertVectorAsync(SqliteConnection connection, long chunkId, float[] embedding, CancellationToken cancellationToken)
    {
        if (embedding.Length != 384)
        {
            return;
        }

        try
        {
            var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO chunk_vectors(rowid, embedding) VALUES($id, $embedding);";
            command.Parameters.AddWithValue("$id", chunkId);
            command.Parameters.AddWithValue("$embedding", JsonSerializer.Serialize(embedding));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch
        {
            // The JSON copy in chunks is authoritative for the MVP fallback path.
        }
    }

    private static double Cosine(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0;
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}
