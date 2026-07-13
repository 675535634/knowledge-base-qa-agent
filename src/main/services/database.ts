import Database from 'better-sqlite3'
import type { ChatRow, Citation, DocumentRow } from '../../shared/contracts'

type ChunkRow = { id: number; document_id: number; ordinal: number; text: string; source_path: string; source_label: string; embedding_json: string }

export class KnowledgeDatabase {
  readonly db: Database.Database
  constructor(path: string) {
    this.db = new Database(path)
    this.db.pragma('journal_mode = WAL'); this.db.pragma('foreign_keys = ON')
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS documents(id INTEGER PRIMARY KEY AUTOINCREMENT,path TEXT NOT NULL,title TEXT NOT NULL,content_hash TEXT NOT NULL UNIQUE,created_at TEXT NOT NULL);
      CREATE TABLE IF NOT EXISTS chunks(id INTEGER PRIMARY KEY AUTOINCREMENT,document_id INTEGER NOT NULL,ordinal INTEGER NOT NULL,text TEXT NOT NULL,source_path TEXT NOT NULL,source_label TEXT NOT NULL,content_hash TEXT NOT NULL,embedding_json TEXT NOT NULL,created_at TEXT NOT NULL,FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE);
      CREATE INDEX IF NOT EXISTS idx_chunks_document_id ON chunks(document_id);
      CREATE TABLE IF NOT EXISTS messages(id INTEGER PRIMARY KEY AUTOINCREMENT,role TEXT NOT NULL,content TEXT NOT NULL,created_at TEXT NOT NULL,provider_id TEXT NOT NULL,model TEXT NOT NULL,citation_chunk_ids TEXT);
      CREATE TABLE IF NOT EXISTS app_meta(key TEXT PRIMARY KEY,value TEXT NOT NULL);
    `)
  }
  hasHash(hash: string): boolean { return !!this.db.prepare('SELECT 1 FROM documents WHERE content_hash=?').get(hash) }
  addDocument(path: string, title: string, hash: string): number {
    return Number(this.db.prepare('INSERT INTO documents(path,title,content_hash,created_at) VALUES(?,?,?,?)').run(path, title, hash, new Date().toISOString()).lastInsertRowid)
  }
  addChunk(documentId: number, ordinal: number, text: string, path: string, label: string, hash: string, embedding: number[]): void {
    this.db.prepare('INSERT INTO chunks(document_id,ordinal,text,source_path,source_label,content_hash,embedding_json,created_at) VALUES(?,?,?,?,?,?,?,?)')
      .run(documentId, ordinal, text, path, label, hash, JSON.stringify(embedding), new Date().toISOString())
  }
  listDocuments(): DocumentRow[] {
    return this.db.prepare(`SELECT d.id,d.path,d.title,d.created_at createdAt,COUNT(c.id) chunks FROM documents d LEFT JOIN chunks c ON c.document_id=d.id GROUP BY d.id ORDER BY d.id DESC`).all() as DocumentRow[]
  }
  removeDocument(id: number): void { this.db.prepare('DELETE FROM documents WHERE id=?').run(id) }
  clearKnowledge(): void { this.db.transaction(() => { this.db.prepare('DELETE FROM chunks').run(); this.db.prepare('DELETE FROM documents').run() })() }
  async ensureEmbeddingIndex(signature: string, embed: (text: string) => Promise<number[]>): Promise<void> {
    const current = this.db.prepare("SELECT value FROM app_meta WHERE key='embedding_signature'").get() as { value: string } | undefined
    if (current?.value === signature) return
    const rows = this.db.prepare('SELECT id,text FROM chunks ORDER BY id').all() as Array<{ id: number; text: string }>
    const update = this.db.prepare('UPDATE chunks SET embedding_json=? WHERE id=?')
    for (const row of rows) update.run(JSON.stringify(await embed(row.text)), row.id)
    this.db.prepare("INSERT OR REPLACE INTO app_meta(key,value) VALUES('embedding_signature',?)").run(signature)
  }
  search(vector: number[], topK: number): Citation[] {
    const rows = this.db.prepare('SELECT id,document_id,ordinal,text,source_path,source_label,embedding_json FROM chunks').all() as ChunkRow[]
    return rows.map((row) => ({ id: row.id, sourceLabel: row.source_label, sourcePath: row.source_path, text: row.text, score: cosine(vector, JSON.parse(row.embedding_json) as number[]) }))
      .sort((a, b) => b.score - a.score).slice(0, topK)
  }
  addMessage(role: 'user' | 'assistant', content: string, provider: string, model: string, citations: number[] = []): void {
    this.db.prepare('INSERT INTO messages(role,content,created_at,provider_id,model,citation_chunk_ids) VALUES(?,?,?,?,?,?)')
      .run(role, content, new Date().toISOString(), provider, model, citations.join(','))
  }
  history(limit = 40): ChatRow[] {
    const rows = this.db.prepare('SELECT id,role,content,created_at createdAt,citation_chunk_ids ids FROM messages ORDER BY id DESC LIMIT ?').all(limit) as Array<ChatRow & { ids: string }>
    return rows.reverse().map(({ ids, ...row }) => ({ ...row, citations: ids ? this.citations(ids) : [] }))
  }
  clearHistory(): void { this.db.prepare('DELETE FROM messages').run() }
  private citations(ids: string): Citation[] {
    const result: Citation[] = []
    for (const id of ids.split(',').map(Number).filter(Boolean)) {
      const row = this.db.prepare('SELECT id,text,source_path sourcePath,source_label sourceLabel FROM chunks WHERE id=?').get(id) as Omit<Citation, 'score'> | undefined
      if (row) result.push({ ...row, score: 0 })
    }
    return result
  }
}

function cosine(a: number[], b: number[]): number {
  if (!a.length || a.length !== b.length) return -1
  let dot = 0, aa = 0, bb = 0
  for (let i = 0; i < a.length; i++) { dot += a[i] * b[i]; aa += a[i] ** 2; bb += b[i] ** 2 }
  return aa && bb ? dot / Math.sqrt(aa * bb) : 0
}
