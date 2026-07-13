import { existsSync, readFileSync, renameSync, writeFileSync } from 'node:fs'
import type { AppSettings } from '../../shared/contracts'
import type { AppPaths } from './paths'
import { providerCatalog } from '../../shared/providerCatalog'

const eightQuestions = ['请概括知识库的主要内容。','有哪些重要信息需要优先了解？','请列出相关流程或操作步骤。','有哪些规则、限制或注意事项？','请整理相关项目、产品或服务清单。','知识库中有哪些常见问题？','请比较文档中的不同方案。','当前问题缺少哪些信息？']
const providers = providerCatalog.map((item) => item.id === 'dashscope' ? { ...item, enabled: true, chatModel: 'qwen-plus', embeddingModel: 'text-embedding-v4', asrModel: 'qwen3-asr-flash' } : { ...item })

export const defaults: AppSettings = {
  assistantName: '通用知识库智能体',
  windowTitle: '通用知识库智能体',
  visitorHeadline: '您好，我是通用知识库智能体',
  greetingText: '您好，我可以根据已导入的知识库回答问题，也支持文字和语音交互。',
  systemPrompt: '你是一个可用于组织、产品、项目与个人资料的通用知识库智能体。涉及可核验信息时只根据知识库上下文回答；上下文不足时明确说明缺少依据，不猜测、不编造。寒暄、感谢和使用帮助等日常对话可直接简短回答。',
  characterPrompt: '你是一位通用、专业、可靠且友好的知识库智能体，不假定任何学校、企业、政府部门或特定行业背景。',
  retrievalTopK: 8,
  quickQuestions: eightQuestions,
  wakeWords: ['助手', '小助手', '你好助手'],
  llm: { baseUrl: 'https://dashscope.aliyuncs.com/compatible-mode/v1', apiKey: '', model: 'qwen-plus', enableThinking: false },
  providers,
  activeProviders: { chat: 'dashscope', embedding: 'local', asr: 'local' },
  embedding: { mode: 'local-onnx', model: 'Xenova/bge-small-zh-v1.5', providerId: 'local' },
  asr: { mode: 'local-onnx', model: 'onnx-community/whisper-small', language: 'zh', providerId: 'local' },
  tts: { speed: 1.3, voice: 0, executablePath: '', modelPath: '' },
  pet: { frameIntervalMs: 140, scale: 1, dialogues: ['您好，需要我帮您查询什么？','点击我可以打开问答窗口。','按 Ctrl+1 可以进入管理员设置。','知识库和语音支持本地运行。'] },
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
    const mergedProviders = providerCatalog.map((item) => {
      const saved = configured.get(item.id)
      return { ...item, ...saved, modelCatalog: { ...item.modelCatalog, ...saved?.modelCatalog } }
    })
    for (const item of value.providers || []) if (!mergedProviders.some((entry) => entry.id === item.id)) mergedProviders.push({ ...item, modelCatalog: item.modelCatalog || { all: [], chat: [], embedding: [], asr: [] } })
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
