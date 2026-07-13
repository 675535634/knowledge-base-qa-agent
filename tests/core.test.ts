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

describe('local fallback', () => {
  it('ships eight visitor presets and the full provider catalog',()=>{expect(defaults.quickQuestions).toHaveLength(8);expect(providerCatalog.length).toBeGreaterThanOrEqual(66)})
  it('creates deterministic normalized 384-dimensional embeddings', () => {
    const first = hashEmbedding('招生 专业 报名')
    const second = hashEmbedding('招生 专业 报名')
    expect(first).toEqual(second)
    expect(first).toHaveLength(384)
    expect(Math.sqrt(first.reduce((sum, value) => sum + value * value, 0))).toBeCloseTo(1, 6)
  })

  it('separates unrelated Chinese text', () => {
    const a = hashEmbedding('学校招生报名材料')
    const b = hashEmbedding('学校招生报名时间')
    const c = hashEmbedding('今天天气晴朗')
    const cosine = (x: number[], y: number[]) => x.reduce((sum, n, i) => sum + n * y[i], 0)
    expect(cosine(a, b)).toBeGreaterThan(cosine(a, c))
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
