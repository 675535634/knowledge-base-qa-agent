import type { AppSettings, DesktopApi } from '../../shared/contracts'
import { providerCatalog } from '../../shared/providerCatalog'

const settings: AppSettings = {
  assistantName: '南宁商贸智能问答助手', windowTitle:'南宁商贸智能问答助手', visitorHeadline: '您好，我是南宁商贸智能问答助手',
  greetingText: '随时为您解答学校、专业、报名与现场服务问题。',
  systemPrompt: '', characterPrompt: '', retrievalTopK: 8,
  quickQuestions: ['报名需要什么条件？什么时候报名？', '学校有哪些专业？哪个专业好就业？', '毕业后可以继续升学吗？','住宿条件怎么样？','有哪些社团？','是否安排实习？','开学带什么材料？','怎么报名？'],
  wakeWords: ['助手'], llm: { baseUrl: 'https://example.com/v1', apiKey: '', model: 'qwen-plus', enableThinking: false },
  providers:providerCatalog.map(p=>p.id==='dashscope'?{...p,name:'阿里百炼',enabled:true,chatModel:'qwen-plus',embeddingModel:'text-embedding-v4',asrModel:'qwen3-asr-flash'}:{...p}),activeProviders:{chat:'dashscope',embedding:'local',asr:'local'},
  embedding: { mode: 'local-onnx', model: 'Xenova/bge-small-zh-v1.5',providerId:'local' }, asr: { mode:'local-onnx',model: 'whisper-small', language: 'zh',providerId:'local' },
  tts: { speed: 1.3, voice: 0,executablePath:'',modelPath:'' }, pet: { frameIntervalMs: 140, scale: 1,dialogues:['您好，需要我帮您查什么？'] },assets:{logoPath:'',petFramesPath:''},adminPinHash:''
}
export function browserMock(): DesktopApi {
  return {
    settings: { get: async () => settings, save: async (value) => Object.assign(settings, value) },
    knowledge: { list: async () => [{ id: 1, title: '2026 招生简章.pdf', path: 'D:/资料/2026 招生简章.pdf', chunks: 24, createdAt: new Date().toISOString() }], import: async () => ({ files: 0, chunks: 0 }), remove: async () => {}, clear: async () => {} },
    chat: { history: async () => [{ id: 1, role: 'assistant', content: '您好，我在。请问想了解什么？', createdAt: new Date().toISOString() }], ask: async () => ({ answer: '这是浏览器预览模式。', citations: [] }), clear: async () => {} },
    speech: { transcribe: async () => '', speak: async () => new Uint8Array(), stop: async () => {} }, auth:{verify:async()=>true,change:async()=>true}, assets:{get:async()=>({logo:'./brand/logo.png',frames:{idle:[],listening:[],thinking:[],speaking:[],error:[]}}),chooseLogo:async()=>'',choosePetFrames:async()=>''},
    app: { runtimeStatus: async () => ({ embedding: '本地 ONNX · bge-small-zh-v1.5', asr: '本地 Whisper ONNX', tts: '本地 VITS · sherpa-onnx-vits-zh-ll', dataDir: 'Data' }), openAdmin: async () => {}, showVisitor: async () => {}, closeVisitor: async () => {}, quit: async () => {} },
    pet: { setState: async () => {}, beginDrag: async () => {}, drag: async () => {},endDrag:async()=>{}, showMenu: async () => {} }, events: { onAvatarState: () => () => {} }
  }
}
