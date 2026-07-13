import type { ProviderConfig, ProviderModelCatalog } from '../../shared/contracts'

const localWithoutKey = new Set(['ollama', 'lmstudio', 'new-api', 'gpustack', 'localai', 'xinference', 'openllm'])

export async function fetchProviderModels(provider: ProviderConfig): Promise<ProviderModelCatalog> {
  if (!provider.baseUrl.trim() && !['github'].includes(provider.id)) throw new Error('请先填写 Base URL')
  if (!provider.apiKey.trim() && !localWithoutKey.has(provider.id)) throw new Error('请先填写 API Key')

  const { url, headers } = modelRequest(provider)
  const response = await fetch(url, { headers, signal: AbortSignal.timeout(20_000) })
  const body = await response.text()
  if (!response.ok) throw new Error(`获取模型失败 (${response.status})：${body.slice(0, 300)}`)

  let json: unknown
  try { json = JSON.parse(body) } catch { throw new Error('服务商返回的模型列表不是有效 JSON') }
  const all = extractModels(json)
  if (!all.length) throw new Error('服务商接口没有返回可用模型')
  return classifyModels(all)
}

function modelRequest(provider: ProviderConfig): { url: string; headers: Record<string, string> } {
  const base = provider.baseUrl.trim().replace(/\/+$/, '')
  if (provider.id === 'ollama') return { url: `${new URL(base).origin}/api/tags`, headers: {} }
  if (provider.id === 'github') return { url: 'https://models.github.ai/catalog/models', headers: bearer(provider.apiKey) }
  if (provider.protocol === 'gemini') {
    return { url: `${base}/models?key=${encodeURIComponent(provider.apiKey)}`, headers: {} }
  }
  if (provider.protocol === 'anthropic') {
    return { url: appendModels(base), headers: { 'x-api-key': provider.apiKey, 'anthropic-version': '2023-06-01' } }
  }
  if (provider.protocol === 'azure-openai') {
    const root = base.replace(/\/openai(?:\/.*)?$/i, '')
    return { url: `${root}/openai/models?api-version=2024-10-21`, headers: { 'api-key': provider.apiKey } }
  }
  return { url: appendModels(base), headers: provider.apiKey ? bearer(provider.apiKey) : {} }
}

function appendModels(base: string): string {
  return /\/models(?:\?|$)/i.test(base) ? base : `${base}/models`
}

function bearer(apiKey: string): Record<string, string> {
  return apiKey ? { Authorization: `Bearer ${apiKey}` } : {}
}

export function extractModels(value: unknown): string[] {
  const rows = findRows(value)
  const models: string[] = []
  for (const row of rows) {
    if (typeof row === 'string') { models.push(stripGeminiPrefix(row)); continue }
    if (!row || typeof row !== 'object' || unavailable(row as Record<string, unknown>)) continue
    const item = row as Record<string, unknown>
    const id = ['id', 'model', 'model_id', 'name', 'baseModelId'].map(key => item[key]).find(x => typeof x === 'string')
    if (typeof id === 'string' && id.trim()) models.push(stripGeminiPrefix(id.trim()))
  }
  return [...new Set(models)].sort((a, b) => a.localeCompare(b, 'zh-CN'))
}

function findRows(value: unknown): unknown[] {
  if (Array.isArray(value)) return value
  if (!value || typeof value !== 'object') return []
  const object = value as Record<string, unknown>
  for (const key of ['data', 'models', 'items', 'results']) if (Array.isArray(object[key])) return object[key] as unknown[]
  return []
}

function unavailable(item: Record<string, unknown>): boolean {
  if (item.archived === true || item.deleted === true || item.disabled === true || item.available === false) return true
  return ['archived', 'deleted', 'disabled', 'unavailable', 'failed'].includes(String(item.status || '').toLowerCase())
}

function stripGeminiPrefix(value: string): string { return value.replace(/^models\//i, '') }

export function classifyModels(all: string[]): ProviderModelCatalog {
  const embedding = all.filter(model => /(?:^|[-_/.])(embed|embedding|bge|e5)(?:[-_/.]|$)|jina-embeddings/i.test(model))
  const asr = all.filter(model => /whisper|transcri|(?:^|[-_/.])asr(?:[-_/.]|$)|speech[-_]?recognition|audio[-_]?speech/i.test(model))
  const excluded = new Set([...embedding, ...asr, ...all.filter(model => /tts|speech[-_]?synth|rerank/i.test(model))])
  const chat = all.filter(model => !excluded.has(model))
  return { all, chat, embedding, asr }
}
