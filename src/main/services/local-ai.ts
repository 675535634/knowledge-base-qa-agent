import { createHash } from 'node:crypto'
import type { AppPaths } from './paths'
import type { AppSettings } from '../../shared/contracts'

type Pipeline = (...args: unknown[]) => Promise<unknown>

export class LocalAiService {
  private embeddingPipeline?: Pipeline
  private effectiveEmbedding = ''
  private asrPipeline?: Pipeline
  private embeddingState = '待加载（首次使用会下载本地模型）'
  private asrState = '待加载（首次使用会下载本地模型）'
  constructor(private readonly paths: AppPaths, private readonly getSettings: () => AppSettings) {}

  status(): { embedding: string; asr: string } { return { embedding: this.embeddingState, asr: this.asrState } }
  embeddingSignature(): string { return this.effectiveEmbedding || (this.getSettings().embedding.mode === 'hash' ? 'hash-384' : this.getSettings().embedding.model) }

  async embed(text: string): Promise<number[]> {
    const settings = this.getSettings()
    if (settings.embedding.mode === 'remote') return this.remoteEmbed(text)
    if (settings.embedding.mode === 'hash') { this.effectiveEmbedding = 'hash-384'; return hashEmbedding(text) }
    try {
      if (!this.embeddingPipeline) {
        this.embeddingState = '正在加载…'
        const transformers = await import('@huggingface/transformers')
        transformers.env.cacheDir = this.paths.models
        this.embeddingPipeline = await transformers.pipeline('feature-extraction', settings.embedding.model, { dtype: 'q8' }) as unknown as Pipeline
        this.embeddingState = `本地 ONNX · ${settings.embedding.model}`
        this.effectiveEmbedding = settings.embedding.model
      }
      const result = await this.embeddingPipeline(text, { pooling: 'mean', normalize: true }) as { data: Float32Array }
      return Array.from(result.data)
    } catch (error) {
      this.embeddingState = `ONNX 不可用，已降级为本地 Hash：${message(error)}`
      this.effectiveEmbedding = 'hash-384'
      return hashEmbedding(text)
    }
  }

  async transcribe(samples: Float32Array): Promise<string> {
    const settings = this.getSettings()
    if (!samples.length) throw new Error('没有收到录音数据')
    if (settings.asr.mode === 'remote') return this.remoteTranscribe(samples)
    try {
      if (!this.asrPipeline) {
        this.asrState = '正在加载…'
        const transformers = await import('@huggingface/transformers')
        transformers.env.cacheDir = this.paths.models
        this.asrPipeline = await transformers.pipeline('automatic-speech-recognition', settings.asr.model, { dtype: 'q8' }) as unknown as Pipeline
        this.asrState = `本地 Whisper ONNX · ${settings.asr.model}`
      }
      const output = await this.asrPipeline(samples, { language: settings.asr.language, task: 'transcribe', chunk_length_s: 30 }) as { text?: string }
      return (output.text || '').trim()
    } catch (error) {
      this.asrState = `本地 ASR 错误：${message(error)}`
      throw new Error(this.asrState)
    }
  }
  private provider(id:string){ const value=this.getSettings().providers.find(x=>x.id===id); if(!value)throw new Error(`找不到服务商：${id}`); return value }
  private async remoteEmbed(text:string):Promise<number[]>{ const s=this.getSettings(),p=this.provider(s.embedding.providerId||s.activeProviders.embedding); const r=await fetch(`${p.baseUrl.replace(/\/$/,'')}/embeddings`,{method:'POST',headers:{'Content-Type':'application/json',Authorization:`Bearer ${p.apiKey}`},body:JSON.stringify({model:p.embeddingModel||s.embedding.model,input:text})}); if(!r.ok)throw new Error(`Embedding 请求失败 (${r.status})：${(await r.text()).slice(0,300)}`); const j=await r.json() as {data?:Array<{embedding:number[]}>}; const v=j.data?.[0]?.embedding;if(!v)throw new Error('Embedding 返回为空');this.effectiveEmbedding=`${p.id}:${p.embeddingModel}`;return v }
  private async remoteTranscribe(samples:Float32Array):Promise<string>{ const s=this.getSettings(),p=this.provider(s.asr.providerId||s.activeProviders.asr); const form=new FormData();form.append('model',p.asrModel||s.asr.model);form.append('language',s.asr.language);form.append('file',new Blob([wav(samples)],{type:'audio/wav'}),'recording.wav');const r=await fetch(`${p.baseUrl.replace(/\/$/,'')}/audio/transcriptions`,{method:'POST',headers:{Authorization:`Bearer ${p.apiKey}`},body:form});if(!r.ok)throw new Error(`ASR 请求失败 (${r.status})：${(await r.text()).slice(0,300)}`);const j=await r.json() as {text?:string};return(j.text||'').trim()}
}

function wav(samples:Float32Array):ArrayBuffer{const b=new ArrayBuffer(44+samples.length*2),v=new DataView(b);const w=(o:number,s:string)=>[...s].forEach((c,i)=>v.setUint8(o+i,c.charCodeAt(0)));w(0,'RIFF');v.setUint32(4,36+samples.length*2,true);w(8,'WAVE');w(12,'fmt ');v.setUint32(16,16,true);v.setUint16(20,1,true);v.setUint16(22,1,true);v.setUint32(24,16000,true);v.setUint32(28,32000,true);v.setUint16(32,2,true);v.setUint16(34,16,true);w(36,'data');v.setUint32(40,samples.length*2,true);samples.forEach((n,i)=>v.setInt16(44+i*2,Math.max(-1,Math.min(1,n))*32767,true));return b}

export function hashEmbedding(text: string, dimensions = 384): number[] {
  const vector = new Array<number>(dimensions).fill(0)
  const normalized = text.toLocaleLowerCase().replace(/\s+/g, '')
  const tokens = [...normalized]
  for (let i = 0; i < tokens.length; i++) {
    for (const width of [1, 2, 3]) {
      const token = tokens.slice(i, i + width).join('')
      if (token.length !== width) continue
      const hash = createHash('sha256').update(token).digest()
      const index = hash.readUInt32LE(0) % dimensions
      vector[index] += (hash[4] & 1) ? 1 : -1
    }
  }
  const norm = Math.sqrt(vector.reduce((sum, n) => sum + n * n, 0)) || 1
  return vector.map((n) => n / norm)
}

function message(error: unknown): string { return error instanceof Error ? error.message : String(error) }
