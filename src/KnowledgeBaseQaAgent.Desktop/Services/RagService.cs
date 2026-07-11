using KnowledgeBaseQaAgent.Desktop.Models;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class RagService
{
    private static readonly TimeSpan RoutePlanningTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan SearchPlanningTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan FollowUpPlanningTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(45);

    private readonly SqliteKnowledgeStore _store;
    private readonly DocumentParser _documentParser;
    private readonly TextChunker _chunker;
    private readonly ProviderRegistry _providers;

    public RagService(
        SqliteKnowledgeStore store,
        DocumentParser documentParser,
        TextChunker chunker,
        ProviderRegistry providers)
    {
        _store = store;
        _documentParser = documentParser;
        _chunker = chunker;
        _providers = providers;
    }

    public async Task<int> ImportDocumentAsync(string path, CancellationToken cancellationToken = default)
    {
        var fileHash = DocumentParser.ComputeFileHash(path);
        if (await _store.HasDocumentHashAsync(fileHash, cancellationToken))
        {
            return 0;
        }

        var parsed = await _documentParser.ParseAsync(path, cancellationToken);
        var chunks = _chunker.Chunk(parsed);
        if (chunks.Count == 0)
        {
            return 0;
        }

        var documentId = await _store.AddDocumentAsync(parsed.Path, parsed.Title, fileHash, cancellationToken);
        var embeddingProvider = _providers.CreateEmbeddingProvider();
        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await embeddingProvider.EmbedAsync(chunk.Text, cancellationToken);
            await _store.AddChunkAsync(
                documentId,
                chunk.Ordinal,
                chunk.Text,
                parsed.Path,
                chunk.SourceLabel,
                DocumentParser.ComputeTextHash(chunk.Text),
                embedding,
                cancellationToken);
        }

        return chunks.Count;
    }

    public async Task<RagAnswer> AskAsync(string question, int topK, CancellationToken cancellationToken = default)
    {
        var embeddingProvider = _providers.CreateEmbeddingProvider();
        var retrievalLimit = RagQueryClassifier.ResolveRetrievalLimit(question, topK);
        var history = (await _store.GetRecentMessagesAsync(12, cancellationToken))
            .Select(message => new ChatTurn(message.Role, message.Content))
            .ToArray();

        var chatProvider = _providers.CreateChatProvider();
        var promptProfile = _providers.CreatePromptProfile(question);
        var searchQuery = question;
        try
        {
            var route = await WithTimeoutAsync(
                linkedToken => chatProvider.PlanAnswerRouteAsync(question, history, promptProfile, linkedToken),
                RoutePlanningTimeout,
                cancellationToken);
            if (route.Mode.Equals("direct", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(route.DirectAnswer))
            {
                var directProviderId = chatProvider.ProviderId;
                var directModel = _providers.GetConfig(chatProvider.ProviderId).Model;
                var directAnswer = AnswerPostProcessor.CleanForVisitor(route.DirectAnswer);
                await _store.AddMessageAsync("user", question, directProviderId, directModel, null, cancellationToken);
                await _store.AddMessageAsync("assistant", directAnswer, directProviderId, directModel, null, cancellationToken);
                ProviderDiagnostics.Info($"Answer route: direct, questionChars={question.Length}, answerChars={directAnswer.Length}");
                return new RagAnswer(directAnswer, []);
            }

            if (!string.IsNullOrWhiteSpace(route.SearchQuery))
            {
                searchQuery = route.SearchQuery;
            }

            ProviderDiagnostics.Info($"Answer route: knowledge, searchQuery={searchQuery}");
        }
        catch (Exception ex) when (IsNonCriticalPlanningFailure(ex, cancellationToken))
        {
            ProviderDiagnostics.Error($"Answer route planning failed, using knowledge retrieval. Type={ex.GetType().Name}, Message={ex.Message}");
        }

        try
        {
            if (searchQuery.Equals(question, StringComparison.OrdinalIgnoreCase))
            {
                searchQuery = await WithTimeoutAsync(
                    linkedToken => chatProvider.CreateSearchQueryAsync(question, history, promptProfile, linkedToken),
                    SearchPlanningTimeout,
                    cancellationToken);
            }
        }
        catch (Exception ex) when (IsNonCriticalPlanningFailure(ex, cancellationToken))
        {
            ProviderDiagnostics.Error($"Search query planning failed, using original question. Type={ex.GetType().Name}, Message={ex.Message}");
        }

        var queryEmbedding = await embeddingProvider.EmbedAsync(searchQuery, cancellationToken);
        var citations = (await _store.SearchAsync(queryEmbedding, retrievalLimit, cancellationToken)).ToList();
        string? followUpQuery = null;
        try
        {
            followUpQuery = await WithTimeoutAsync(
                linkedToken => chatProvider.CreateFollowUpSearchQueryAsync(question, citations, history, promptProfile, linkedToken),
                FollowUpPlanningTimeout,
                cancellationToken);
        }
        catch (Exception ex) when (IsNonCriticalPlanningFailure(ex, cancellationToken))
        {
            ProviderDiagnostics.Error($"Follow-up retrieval planning failed, using first retrieval only. Type={ex.GetType().Name}, Message={ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(followUpQuery) &&
            !followUpQuery.Equals(searchQuery, StringComparison.OrdinalIgnoreCase))
        {
            var followUpEmbedding = await embeddingProvider.EmbedAsync(followUpQuery, cancellationToken);
            var followUpCitations = await _store.SearchAsync(followUpEmbedding, retrievalLimit, cancellationToken);
            citations = MergeCitations(citations, followUpCitations, retrievalLimit);
        }

        await _store.AddMessageAsync("user", question, chatProvider.ProviderId, _providers.GetConfig(chatProvider.ProviderId).Model, null, cancellationToken);
        var answerProviderId = chatProvider.ProviderId;
        var answerModel = _providers.GetConfig(chatProvider.ProviderId).Model;
        string answer;
        try
        {
            answer = await WithTimeoutAsync(
                linkedToken => chatProvider.CompleteAsync(new ChatRequest(question, citations, history, promptProfile), linkedToken),
                AnswerTimeout,
                cancellationToken);
        }
        catch (Exception ex) when (IsTimeoutFailure(ex, cancellationToken))
        {
            ProviderDiagnostics.Error($"Chat answer timed out, using local context fallback. Type={ex.GetType().Name}, Message={ex.Message}");
            var localProvider = new LocalContextChatProvider(_providers.GetConfig("local-context-chat"));
            answer = "云端模型响应超时，先根据当前知识库召回内容给出简要结果："
                + Environment.NewLine
                + Environment.NewLine
                + await localProvider.CompleteAsync(new ChatRequest(question, citations, history, promptProfile), cancellationToken);
            answerProviderId = localProvider.ProviderId;
            answerModel = _providers.GetConfig(localProvider.ProviderId).Model;
        }

        answer = AnswerPostProcessor.CleanForVisitor(answer);
        var citationIds = string.Join(",", citations.Select(citation => citation.Chunk.Id));
        await _store.AddMessageAsync("assistant", answer, answerProviderId, answerModel, citationIds, cancellationToken);
        return new RagAnswer(answer, citations);
    }

    private static async Task<T> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            return await operation(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds:0} seconds.");
        }
    }

    private static bool IsNonCriticalPlanningFailure(Exception ex, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (IsTimeoutFailure(ex, cancellationToken) || ex is ProviderHttpException || ex is InvalidOperationException);

    private static bool IsTimeoutFailure(Exception ex, CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        (ex is TimeoutException ||
            ex is TaskCanceledException ||
            ex is OperationCanceledException ||
            ex.InnerException is TimeoutException);

    private static List<SearchResult> MergeCitations(
        IReadOnlyList<SearchResult> first,
        IReadOnlyList<SearchResult> second,
        int limit)
    {
        var merged = new Dictionary<long, SearchResult>();
        foreach (var result in first.Concat(second))
        {
            if (!merged.TryGetValue(result.Chunk.Id, out var existing) || result.Score > existing.Score)
            {
                merged[result.Chunk.Id] = result;
            }
        }

        return merged.Values
            .OrderByDescending(result => result.Score)
            .Take(Math.Max(1, limit))
            .ToList();
    }
}
