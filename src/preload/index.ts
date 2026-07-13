import { contextBridge, ipcRenderer } from 'electron'
import type { AppSettings, AvatarState, DesktopApi } from '../shared/contracts'

const api: DesktopApi = {
  settings: { get: () => ipcRenderer.invoke('settings:get'), save: (value: AppSettings) => ipcRenderer.invoke('settings:save', value) },
  providers: { listModels: (provider) => ipcRenderer.invoke('providers:listModels', provider) },
  knowledge: { list: () => ipcRenderer.invoke('knowledge:list'), import: () => ipcRenderer.invoke('knowledge:import'), remove: (id) => ipcRenderer.invoke('knowledge:remove', id), clear: () => ipcRenderer.invoke('knowledge:clear') },
  chat: { history: () => ipcRenderer.invoke('chat:history'), ask: (question) => ipcRenderer.invoke('chat:ask', question), clear: () => ipcRenderer.invoke('chat:clear') },
  speech: { transcribe: (samples) => ipcRenderer.invoke('speech:transcribe', samples), speak: (text) => ipcRenderer.invoke('speech:speak', text), stop: () => ipcRenderer.invoke('speech:stop') },
  auth: { verify: (pin) => ipcRenderer.invoke('auth:verify',pin), change: (currentPin,newPin) => ipcRenderer.invoke('auth:change',currentPin,newPin) },
  assets: { get: () => ipcRenderer.invoke('assets:get'), chooseLogo: () => ipcRenderer.invoke('assets:chooseLogo'), choosePetFrames: () => ipcRenderer.invoke('assets:choosePetFrames') },
  app: { runtimeStatus: () => ipcRenderer.invoke('app:runtimeStatus'), openAdmin: () => ipcRenderer.invoke('app:openAdmin'), showVisitor: () => ipcRenderer.invoke('app:showVisitor'), closeVisitor: () => ipcRenderer.invoke('app:closeVisitor'), quit: () => ipcRenderer.invoke('app:quit') },
  pet: { setState: (state) => ipcRenderer.invoke('pet:setState', state), beginDrag: () => ipcRenderer.invoke('pet:beginDrag'), drag: () => ipcRenderer.invoke('pet:drag'), endDrag: () => ipcRenderer.invoke('pet:endDrag'), showMenu: () => ipcRenderer.invoke('pet:showMenu') },
  events: { onAvatarState(callback: (state: AvatarState) => void) { const handler = (_: unknown, state: AvatarState) => callback(state); ipcRenderer.on('avatar:state', handler); return () => { ipcRenderer.removeListener('avatar:state', handler) } } }
}
contextBridge.exposeInMainWorld('desktop', api)
