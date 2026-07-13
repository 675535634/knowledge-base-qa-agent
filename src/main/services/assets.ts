import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { extname, join } from 'node:path'
import type { AppSettings, AvatarState } from '../../shared/contracts'
import type { AppPaths } from './paths'

const states: AvatarState[] = ['idle','listening','thinking','speaking','error']
export class AssetService {
  constructor(private paths: AppPaths, private getSettings: () => AppSettings) {}
  get(): { logo: string; frames: Record<AvatarState,string[]> } {
    const settings=this.getSettings(); const defaultLogo=join(this.paths.resources,'brand','logo.png')
    const logo=dataUrl(existsSync(settings.assets.logoPath)?settings.assets.logoPath:defaultLogo)
    const frames={} as Record<AvatarState,string[]>
    for(const state of states){
      const custom=join(settings.assets.petFramesPath||'',state)
      const folder=existsSync(custom)?custom:join(this.paths.resources,'pet',state)
      frames[state]=images(folder).map(dataUrl)
    }
    return {logo,frames}
  }
}
function images(folder:string):string[]{ if(!existsSync(folder))return[]; return readdirSync(folder).filter(x=>['.png','.jpg','.jpeg','.webp','.gif'].includes(extname(x).toLowerCase())).sort().map(x=>join(folder,x)) }
function dataUrl(path:string):string{ if(!path||!existsSync(path))return''; const ext=extname(path).toLowerCase(); const mime=ext==='.svg'?'image/svg+xml':ext==='.jpg'||ext==='.jpeg'?'image/jpeg':ext==='.webp'?'image/webp':ext==='.gif'?'image/gif':'image/png'; return `data:${mime};base64,${readFileSync(path).toString('base64')}` }
