import { app, BrowserWindow, dialog, globalShortcut, ipcMain, Menu, screen, Tray } from 'electron'
import { join } from 'node:path'
import { createHash } from 'node:crypto'
import { createPaths } from './services/paths'
import { SettingsStore } from './services/settings'
import { KnowledgeDatabase } from './services/database'
import { LocalAiService } from './services/local-ai'
import { DocumentService } from './services/documents'
import { LlmService } from './services/llm'
import { RagService } from './services/rag'
import { TtsService } from './services/tts'
import { SecretStore } from './services/secrets'
import { AssetService } from './services/assets'
import { fetchProviderModels } from './services/provider-models'
import type { AppSettings, AvatarState, ProviderConfig } from '../shared/contracts'

let visitor: BrowserWindow | undefined, admin: BrowserWindow | undefined, pet: BrowserWindow | undefined
let tray: Tray | undefined, appMenu: Menu | undefined
let dragOffset = { x: 0, y: 0 }
let dragStart: { cursor:{x:number;y:number}; window:{x:number;y:number} } | undefined
let quitting = false

function webPreferences() { return { preload: join(__dirname, '../preload/index.mjs'), sandbox: true, contextIsolation: true, nodeIntegration: false } }
function load(window: BrowserWindow, view: 'visitor' | 'admin' | 'pet'): void {
  const query = `view=${view}`
  if (process.env.ELECTRON_RENDERER_URL) window.loadURL(`${process.env.ELECTRON_RENDERER_URL}?${query}`)
  else window.loadFile(join(__dirname, '../renderer/index.html'), { query: { view } })
}
function attachAdminShortcut(window: BrowserWindow): void {
  window.webContents.on('before-input-event', (event, input) => {
    if (input.type === 'keyDown' && input.control && input.key === '1') { event.preventDefault(); showAdmin() }
  })
}
function createWindows(): void {
  visitor = new BrowserWindow({ width: 1280, height: 820, minWidth: 960, minHeight: 650, show: false, backgroundColor: '#f5f7ff', autoHideMenuBar: true, webPreferences: webPreferences() })
  visitor.on('close', (event) => { if (!quitting) { event.preventDefault(); visitor?.hide() } })
  attachAdminShortcut(visitor)
  load(visitor, 'visitor')
  visitor.once('ready-to-show', () => visitor?.show())
  const area = screen.getPrimaryDisplay().workArea
  pet = new BrowserWindow({ width: 220, height: 250, x: area.x + area.width - 240, y: area.y + area.height - 270, transparent: true, frame: false, resizable: false, alwaysOnTop: true, skipTaskbar: true, hasShadow: false, webPreferences: webPreferences() })
  attachAdminShortcut(pet)
  pet.setAlwaysOnTop(true, 'floating'); load(pet, 'pet')
}
function showAdmin(): void {
  if (!admin || admin.isDestroyed()) {
    admin = new BrowserWindow({ width: 1180, height: 780, minWidth: 1000, minHeight: 680, backgroundColor: '#f6f7fb', autoHideMenuBar: true, webPreferences: webPreferences() })
    attachAdminShortcut(admin)
    admin.on('closed', () => { admin = undefined }); load(admin, 'admin')
  } else admin.show(); admin.focus()
}
function showVisitor(): void { visitor?.show(); visitor?.focus() }
function quitApplication(): void { quitting = true; app.quit() }
async function createTray(): Promise<void> {
  appMenu = Menu.buildFromTemplate([
    { label: '打开问答窗口', click: showVisitor },
    { label: '管理员控制台  Ctrl+1', click: showAdmin },
    { type: 'separator' },
    { label: '退出', click: quitApplication }
  ])
  const systemAppIcon = await app.getFileIcon(process.execPath, { size: 'small' })
  tray = new Tray(systemAppIcon)
  tray.setToolTip('通用知识库智能体')
  tray.setContextMenu(appMenu)
  tray.on('click', showVisitor)
}
function broadcastState(state: AvatarState): void { for (const win of [visitor, pet]) if (win && !win.isDestroyed()) win.webContents.send('avatar:state', state) }

app.whenReady().then(() => {
  const paths = createPaths(); const settingsStore = new SettingsStore(paths); const secrets = new SecretStore(paths); let settings = settingsStore.load()
  const plaintextKey = settings.llm.apiKey
  settings.llm.apiKey = secrets.get('llm-api-key', 'llm-aliyun', 'openai-compatible') || plaintextKey
  if (plaintextKey) { secrets.set('llm-api-key', plaintextKey); settingsStore.save({ ...settings, llm: { ...settings.llm, apiKey: '' } }) }
  settings.providers=settings.providers.map(p=>({...p,apiKey:secrets.get(`provider-${p.id}`)||(p.id===settings.activeProviders.chat?settings.llm.apiKey:'')}))
  const db = new KnowledgeDatabase(paths.database); const ai = new LocalAiService(paths, () => settings)
  const documents = new DocumentService(db, ai); const llm = new LlmService(() => settings); const rag = new RagService(db, ai, llm, () => settings); const tts = new TtsService(paths, () => settings); const assets=new AssetService(paths,()=>settings)

  ipcMain.handle('settings:get', () => settings)
  ipcMain.handle('providers:listModels', (_e, provider: ProviderConfig) => fetchProviderModels(provider))
  ipcMain.handle('settings:save', (_e, value: AppSettings) => {
    secrets.set('llm-api-key', value.llm.apiKey)
    for(const provider of value.providers)secrets.set(`provider-${provider.id}`,provider.apiKey)
    const cleanProviders=value.providers.map(p=>({...p,apiKey:''}))
    const saved = settingsStore.save({ ...value, providers:cleanProviders, llm: { ...value.llm, apiKey: '' } })
    settings = { ...saved, providers:saved.providers.map(p=>({...p,apiKey:value.providers.find(x=>x.id===p.id)?.apiKey||''})), llm: { ...saved.llm, apiKey: value.llm.apiKey } }
    for(const win of [visitor,admin])if(win&&!win.isDestroyed())win.setTitle(settings.windowTitle)
    return settings
  })
  ipcMain.handle('auth:verify',(_e,pin:string)=>hashPin(pin)===settings.adminPinHash)
  ipcMain.handle('auth:change',(_e,current:string,next:string)=>{if(hashPin(current)!==settings.adminPinHash||next.length<6)return false;settings={...settings,adminPinHash:hashPin(next)};settingsStore.save({...settings,llm:{...settings.llm,apiKey:''},providers:settings.providers.map(p=>({...p,apiKey:''}))});return true})
  ipcMain.handle('assets:get',()=>assets.get())
  ipcMain.handle('assets:chooseLogo',async()=>{const r=await dialog.showOpenDialog({properties:['openFile'],filters:[{name:'图片',extensions:['png','jpg','jpeg','webp','gif','svg','ico']}]});return r.canceled?'':r.filePaths[0]})
  ipcMain.handle('assets:choosePetFrames',async()=>{const r=await dialog.showOpenDialog({properties:['openDirectory']});return r.canceled?'':r.filePaths[0]})
  ipcMain.handle('knowledge:list', () => db.listDocuments())
  ipcMain.handle('knowledge:import', async () => {
    const result = await dialog.showOpenDialog({ properties: ['openFile', 'multiSelections'], filters: [{ name: '知识库文档', extensions: ['pdf', 'docx', 'txt', 'md', 'markdown', 'html', 'htm'] }] })
    return result.canceled ? { files: 0, chunks: 0 } : documents.importFiles(result.filePaths)
  })
  ipcMain.handle('knowledge:remove', (_e, id: number) => db.removeDocument(id)); ipcMain.handle('knowledge:clear', () => db.clearKnowledge())
  ipcMain.handle('chat:history', () => db.history()); ipcMain.handle('chat:clear', () => db.clearHistory())
  ipcMain.handle('chat:ask', async (_e, question: string) => { broadcastState('thinking'); try { const result = await rag.ask(question); broadcastState('idle'); return result } catch (error) { broadcastState('error'); throw error } })
  ipcMain.handle('speech:transcribe', async (_e, value: ArrayBuffer | Float32Array) => { broadcastState('listening'); try { return await ai.transcribe(value instanceof Float32Array ? value : new Float32Array(value)) } finally { broadcastState('idle') } })
  ipcMain.handle('speech:speak', async (_e, text: string) => { broadcastState('speaking'); try { return await tts.synthesize(text) } finally { broadcastState('idle') } })
  ipcMain.handle('speech:stop', () => tts.stop())
  ipcMain.handle('app:runtimeStatus', () => ({ ...ai.status(), tts: tts.status(), dataDir: paths.root }))
  ipcMain.handle('app:openAdmin', () => showAdmin()); ipcMain.handle('app:showVisitor', showVisitor); ipcMain.handle('app:closeVisitor', () => visitor?.hide()); ipcMain.handle('app:quit', quitApplication)
  ipcMain.handle('pet:setState', (_e, state: AvatarState) => broadcastState(state))
  ipcMain.handle('pet:beginDrag', () => { if(pet)dragStart={cursor:screen.getCursorScreenPoint(),window:pet.getBounds()} })
  ipcMain.handle('pet:drag', () => {if(pet&&dragStart){const p=screen.getCursorScreenPoint();pet.setPosition(dragStart.window.x+p.x-dragStart.cursor.x,dragStart.window.y+p.y-dragStart.cursor.y)}})
  ipcMain.handle('pet:endDrag',()=>{dragStart=undefined})
  ipcMain.handle('pet:showMenu', () => appMenu?.popup({ window: pet }))
  createWindows()
  void createTray()
  globalShortcut.register('Control+1', showAdmin)
})

app.on('before-quit', () => { quitting = true; globalShortcut.unregisterAll(); tray?.destroy(); tray = undefined })
app.on('window-all-closed', () => { /* tray-like desktop pet keeps the app alive */ })
function hashPin(pin:string):string{return createHash('sha256').update(pin.trim()).digest('hex').toUpperCase()}
