import { describe, expect, it } from 'vitest'
import { existsSync, mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { hashEmbedding } from '../src/main/services/local-ai'
import { SecretStore } from '../src/main/services/secrets'
import type { AppPaths } from '../src/main/services/paths'
import { defaults } from '../src/main/services/settings'
import { providerCatalog } from '../src/shared/providerCatalog'
import { TtsService } from '../src/main/services/tts'
import { resolve } from 'node:path'
import { classifyModels, extractModels } from '../src/main/services/provider-models'

describe('local fallback', () => {
  it('ships eight visitor presets and the full provider catalog',()=>{expect(defaults.quickQuestions).toHaveLength(8);expect(providerCatalog.length).toBeGreaterThanOrEqual(66)})
  it('creates deterministic normalized 384-dimensional embeddings', () => {
    const first = hashEmbedding('产品 功能 流程')
    const second = hashEmbedding('产品 功能 流程')
    expect(first).toEqual(second)
    expect(first).toHaveLength(384)
    expect(Math.sqrt(first.reduce((sum, value) => sum + value * value, 0))).toBeCloseTo(1, 6)
  })

  it('separates unrelated Chinese text', () => {
    const a = hashEmbedding('产品申请所需材料')
    const b = hashEmbedding('产品申请办理时间')
    const c = hashEmbedding('今天天气晴朗')
    const cosine = (x: number[], y: number[]) => x.reduce((sum, n, i) => sum + n * y[i], 0)
    expect(cosine(a, b)).toBeGreaterThan(cosine(a, c))
  })
})

describe('provider model discovery', () => {
  it('parses provider and Ollama responses while removing unavailable models', () => {
    expect(extractModels({ data: [{ id: 'gpt-live' }, { id: 'gpt-removed', archived: true }] })).toEqual(['gpt-live'])
    expect(extractModels({ models: [{ name: 'qwen2.5:latest' }, { model: 'whisper:latest' }] })).toEqual(['qwen2.5:latest', 'whisper:latest'])
  })

  it('separates chat, embedding and ASR model dropdowns', () => {
    const catalog = classifyModels(['qwen-plus', 'text-embedding-v4', 'qwen3-asr-flash', 'whisper-1', 'qwen-tts'])
    expect(catalog.chat).toEqual(['qwen-plus'])
    expect(catalog.embedding).toEqual(['text-embedding-v4'])
    expect(catalog.asr).toEqual(['qwen3-asr-flash', 'whisper-1'])
  })
})

describe('portable secrets', () => {
  it('round-trips AES-GCM secrets without plaintext settings', () => {
    const root = mkdtempSync(join(tmpdir(), 'kbqa-secret-'))
    const paths = { root, secrets: join(root, 'secrets.json') } as AppPaths
    try {
      const store = new SecretStore(paths); store.set('llm-api-key', 'test-secret')
      expect(new SecretStore(paths).get('llm-api-key')).toBe('test-secret')
      expect(require('node:fs').readFileSync(paths.secrets, 'utf8')).not.toContain('test-secret')
    } finally { rmSync(root, { recursive: true, force: true }) }
  })
})

const hasLocalTtsRuntime = existsSync(resolve('resources/runtime/tts/bin/sherpa-onnx-offline-tts.exe'))

describe.skipIf(!hasLocalTtsRuntime)('VITS runtime',()=>{
  it('returns a playable RIFF/WAVE byte stream',async()=>{const root=resolve('resources/runtime/tts');const service=new TtsService({tts:root} as AppPaths,()=>defaults);const bytes=await service.synthesize('语音测试正常');expect(Buffer.from(bytes).subarray(0,4).toString()).toBe('RIFF');expect(Buffer.from(bytes).subarray(8,12).toString()).toBe('WAVE');expect(bytes.byteLength).toBeGreaterThan(1000)},15000)
})
