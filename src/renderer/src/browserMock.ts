import type { AppSettings, DesktopApi } from '../../shared/contracts'
import { providerCatalog } from '../../shared/providerCatalog'

const settings: AppSettings = {
  assistantName: '通用知识库智能体', windowTitle:'通用知识库智能体', visitorHeadline: '您好，我是通用知识库智能体',
  greetingText: '我可以根据已导入的知识库回答问题。',
  systemPrompt: '', characterPrompt: '', retrievalTopK: 8,
  quickQuestions: ['请概括知识库内容。','有哪些重要信息？','请列出操作步骤。','有哪些注意事项？','请整理项目清单。','有哪些常见问题？','请比较不同方案。','当前缺少哪些信息？'],
  wakeWords: ['助手'], llm: { baseUrl: 'https://example.com/v1', apiKey: '', model: 'qwen-plus', enableThinking: false },
  providers:providerCatalog.map(p=>p.id==='dashscope'?{...p,name:'阿里百炼',enabled:true,chatModel:'qwen-plus',embeddingModel:'text-embedding-v4',asrModel:'qwen3-asr-flash'}:{...p}),activeProviders:{chat:'dashscope',embedding:'local',asr:'local'},
  embedding: { mode: 'local-onnx', model: 'Xenova/bge-small-zh-v1.5',providerId:'local' }, asr: { mode:'local-onnx',model: 'whisper-small', language: 'zh',providerId:'local' },
  tts: { speed: 1.3, voice: 0,executablePath:'',modelPath:'' }, pet: { frameIntervalMs: 140, scale: 1,dialogues:['您好，需要我帮您查什么？'] },assets:{logoPath:'',petFramesPath:''},adminPinHash:''
}
export function browserMock(): DesktopApi {
  return {
    settings: { get: async () => settings, save: async (value) => Object.assign(settings, value) },
    providers: { listModels: async () => ({ all:['qwen-plus','text-embedding-v4','qwen3-asr-flash'],chat:['qwen-plus'],embedding:['text-embedding-v4'],asr:['qwen3-asr-flash'] }) },
    knowledge: { list: async () => [{ id: 1, title: '示例知识.md', path: 'C:/Documents/example.md', chunks: 24, createdAt: new Date().toISOString() }], import: async () => ({ files: 0, chunks: 0 }), remove: async () => {}, clear: async () => {} },
    chat: { history: async () => [{ id: 1, role: 'assistant', content: '您好，我在。请问想了解什么？', createdAt: new Date().toISOString() }], ask: async () => ({ answer: '这是浏览器预览模式。', citations: [] }), clear: async () => {} },
    speech: { transcribe: async () => '', speak: async () => new Uint8Array(), stop: async () => {} }, auth:{verify:async()=>true,change:async()=>true}, assets:{get:async()=>({logo:'',frames:{idle:[],listening:[],thinking:[],speaking:[],error:[]}}),chooseLogo:async()=>'',choosePetFrames:async()=>''},
    app: { runtimeStatus: async () => ({ embedding: '本地 ONNX · bge-small-zh-v1.5', asr: '本地 Whisper ONNX', tts: '本地 VITS · sherpa-onnx-vits-zh-ll', dataDir: 'Data' }), openAdmin: async () => {}, showVisitor: async () => {}, closeVisitor: async () => {}, quit: async () => {} },
    pet: { setState: async () => {}, beginDrag: async () => {}, drag: async () => {},endDrag:async()=>{}, showMenu: async () => {} }, events: { onAvatarState: () => () => {} }
  }
}
