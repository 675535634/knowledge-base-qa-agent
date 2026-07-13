export type AvatarState = 'idle' | 'listening' | 'thinking' | 'speaking' | 'error'

export interface LlmSettings {
  baseUrl: string
  apiKey: string
  model: string
  enableThinking: boolean
}
export type ProviderProtocol = 'openai' | 'anthropic' | 'gemini' | 'azure-openai' | 'custom'
export interface ProviderConfig {
  id: string; name: string; protocol: ProviderProtocol; baseUrl: string; apiKey: string
  chatModel: string; embeddingModel: string; asrModel: string; enabled: boolean
}

export interface AppSettings {
  assistantName: string
  windowTitle: string
  visitorHeadline: string
  greetingText: string
  systemPrompt: string
  characterPrompt: string
  retrievalTopK: number
  quickQuestions: string[]
  wakeWords: string[]
  llm: LlmSettings
  providers: ProviderConfig[]
  activeProviders: { chat: string; embedding: string; asr: string }
  embedding: { mode: 'local-onnx' | 'hash' | 'remote'; model: string; providerId: string }
  asr: { mode: 'local-onnx' | 'remote'; model: string; language: string; providerId: string }
  tts: { speed: number; voice: number; executablePath: string; modelPath: string }
  pet: { frameIntervalMs: number; scale: number; dialogues: string[] }
  assets: { logoPath: string; petFramesPath: string }
  adminPinHash: string
}

export interface DocumentRow { id: number; path: string; title: string; createdAt: string; chunks: number }
export interface ChatRow { id: number; role: 'user' | 'assistant'; content: string; createdAt: string; citations?: Citation[] }
export interface Citation { id: number; sourceLabel: string; sourcePath: string; text: string; score: number }
export interface AskResult { answer: string; citations: Citation[] }
export interface RuntimeStatus { embedding: string; asr: string; tts: string; dataDir: string }

export interface DesktopApi {
  settings: { get(): Promise<AppSettings>; save(value: AppSettings): Promise<AppSettings> }
  knowledge: { list(): Promise<DocumentRow[]>; import(): Promise<{ files: number; chunks: number }>; remove(id: number): Promise<void>; clear(): Promise<void> }
  chat: { history(): Promise<ChatRow[]>; ask(question: string): Promise<AskResult>; clear(): Promise<void> }
  speech: { transcribe(samples: Float32Array): Promise<string>; speak(text: string): Promise<Uint8Array>; stop(): Promise<void> }
  auth: { verify(pin: string): Promise<boolean>; change(currentPin: string, newPin: string): Promise<boolean> }
  assets: { get(): Promise<{ logo: string; frames: Record<AvatarState, string[]> }>; chooseLogo(): Promise<string>; choosePetFrames(): Promise<string> }
  app: { runtimeStatus(): Promise<RuntimeStatus>; openAdmin(): Promise<void>; showVisitor(): Promise<void>; closeVisitor(): Promise<void>; quit(): Promise<void> }
  pet: { setState(state: AvatarState): Promise<void>; beginDrag(): Promise<void>; drag(): Promise<void>; endDrag(): Promise<void>; showMenu(): Promise<void> }
  events: { onAvatarState(callback: (state: AvatarState) => void): () => void }
}
