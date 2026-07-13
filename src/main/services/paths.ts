import { app } from 'electron'
import { existsSync, mkdirSync } from 'node:fs'
import { join } from 'node:path'

export interface AppPaths { root: string; settings: string; secrets: string; database: string; logs: string; resources: string; models: string; tts: string }

export function createPaths(): AppPaths {
  const portable = process.argv.includes('--portable') || existsSync(join(process.resourcesPath, 'portable.flag')) || existsSync(join(app.getAppPath(), 'portable.flag'))
  const portableDirectory = process.env.PORTABLE_EXECUTABLE_DIR || join(process.resourcesPath, '..')
  const root = portable ? join(portableDirectory, 'Data') : app.getPath('userData')
  const resources = app.isPackaged ? join(process.resourcesPath, 'resources') : join(app.getAppPath(), 'resources')
  const paths = {
    root, settings: join(root, 'settings.json'), secrets: join(root, 'secrets.json'),
    database: join(root, 'knowledge.db'), logs: join(root, 'logs'), resources,
    models: join(resources, 'runtime', 'models'), tts: join(resources, 'runtime', 'tts')
  }
  for (const dir of [root, paths.logs, paths.models]) mkdirSync(dir, { recursive: true })
  return paths
}
