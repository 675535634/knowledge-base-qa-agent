import { existsSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import type { AppSettings } from '../../shared/contracts'
import type { AppPaths } from './paths'
import { providerCatalog } from '../../shared/providerCatalog'

const eightQuestions = ['报读需要什么条件？什么时候报名？','学校有哪些专业？哪个专业好就业？','毕业后可以继续升学吗？有哪些途径？','学校管理和住宿条件怎么样？','学校有哪些社团或课外活动？','学校是否安排实习和就业？','开学需要携带什么材料？','怎么报名？有接待或接送服务吗？']
const providers = providerCatalog.map((item) => item.id === 'dashscope' ? { ...item, enabled: true, chatModel: 'qwen-plus', embeddingModel: 'text-embedding-v4', asrModel: 'qwen3-asr-flash' } : { ...item })

export const defaults: AppSettings = {
  assistantName: '南宁商贸智能问答助手',
  windowTitle: '南宁商贸智能问答助手',
  visitorHeadline: '您好，我是南宁商贸智能问答助手',
  greetingText: '您好，请问有什么需要了解的？可以选择常见问题，也可以直接语音提问。',
  systemPrompt: '你是一个面向触屏现场服务的知识库问答助手。事实问题只根据提供的知识库上下文回答；上下文不足时明确说明缺少依据，不得编造。寒暄、感谢、唤醒等不需要知识库。回答简洁、礼貌，适合现场访客理解。',
  characterPrompt: '你是一位专业、可靠、亲切的现场咨询助手。',
  retrievalTopK: 8,
  quickQuestions: eightQuestions,
  wakeWords: ['助手', '小助手', '你好助手'],
  llm: { baseUrl: 'https://dashscope.aliyuncs.com/compatible-mode/v1', apiKey: '', model: 'qwen-plus', enableThinking: false },
  providers,
  activeProviders: { chat: 'dashscope', embedding: 'local', asr: 'local' },
  embedding: { mode: 'local-onnx', model: 'Xenova/bge-small-zh-v1.5', providerId: 'local' },
  asr: { mode: 'local-onnx', model: 'onnx-community/whisper-small', language: 'zh', providerId: 'local' },
  tts: { speed: 1.3, voice: 0, executablePath: '', modelPath: '' },
  pet: { frameIntervalMs: 140, scale: 1, dialogues: ['您好，需要我帮您查什么？','点击我就可以打开问答窗口。','也可以按 Ctrl+1 进入管理员设置。','知识库和语音都支持本地运行。'] },
  assets: { logoPath: '', petFramesPath: '' },
  adminPinHash: '8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92'
}

export class SettingsStore {
  constructor(private readonly paths: AppPaths) {}
  load(): AppSettings {
    if (!existsSync(this.paths.settings)) return this.save(defaults)
    try {
      const raw = JSON.parse(readFileSync(this.paths.settings, 'utf8')) as Record<string, unknown>
      const value = 'llm' in raw ? raw as unknown as AppSettings : this.migrateLegacy(raw)
      return this.save(this.normalize(value))
    } catch {
      renameSync(this.paths.settings, `${this.paths.settings}.invalid-${Date.now()}`)
      return this.save(defaults)
    }
  }
  save(value: AppSettings): AppSettings {
    const normalized = this.normalize(value)
    writeFileSync(this.paths.settings, JSON.stringify(normalized, null, 2), 'utf8')
    return normalized
  }
  private normalize(value: AppSettings): AppSettings {
    const configured = new Map((value.providers || []).map((item) => [item.id, item]))
    const mergedProviders = providerCatalog.map((item) => ({ ...item, ...(configured.get(item.id) || {}) }))
    for (const item of value.providers || []) if (!mergedProviders.some((entry) => entry.id === item.id)) mergedProviders.push(item)
    const questions = (value.quickQuestions || []).map(String).filter(Boolean)
    for (const question of eightQuestions) if (questions.length < 8 && !questions.includes(question)) questions.push(question)
    return {
      ...defaults, ...value,
      llm: { ...defaults.llm, ...value.llm }, embedding: { ...defaults.embedding, ...value.embedding },
      asr: { ...defaults.asr, ...value.asr }, tts: { ...defaults.tts, ...value.tts }, pet: { ...defaults.pet, ...value.pet }, assets: { ...defaults.assets, ...value.assets }, activeProviders: { ...defaults.activeProviders, ...value.activeProviders }, providers: mergedProviders,
      retrievalTopK: Math.max(1, Math.min(30, Number(value.retrievalTopK) || defaults.retrievalTopK)),
      quickQuestions: questions.slice(0, 8)
    }
  }
  private migrateLegacy(raw: Record<string, unknown>): AppSettings {
    const providers = Array.isArray(raw.providers) ? raw.providers as Array<Record<string, unknown>> : []
    const chat = providers.find((p) => p.providerId === 'openai-chat')
    const options = (chat?.options || {}) as Record<string, string>
    return {
      ...defaults,
      assistantName: String(raw.assistantName || defaults.assistantName),
      visitorHeadline: String(raw.visitorHeadline || defaults.visitorHeadline),
      greetingText: String(raw.greetingText || defaults.greetingText),
      systemPrompt: String(raw.systemPrompt || defaults.systemPrompt),
      characterPrompt: String(raw.characterPrompt || defaults.characterPrompt),
      retrievalTopK: Number(raw.retrievalTopK || defaults.retrievalTopK),
      quickQuestions: Array.isArray(raw.quickQuestions) ? raw.quickQuestions.map(String) : defaults.quickQuestions,
      wakeWords: Array.isArray(raw.wakeWords) ? raw.wakeWords.map(String) : defaults.wakeWords,
      llm: { ...defaults.llm, baseUrl: String(options.baseUrl || defaults.llm.baseUrl), model: String(chat?.model || defaults.llm.model) }
    }
  }
}
