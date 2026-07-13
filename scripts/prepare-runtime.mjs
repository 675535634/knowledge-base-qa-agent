import { cp, mkdir, stat } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const legacy = resolve(root, '..', 'knowledge-base-qa-agent', 'artifacts', 'portable', 'KnowledgeBaseQaAgent')
const source = process.argv[2] ? resolve(process.argv[2]) : join(legacy, 'Tools', 'VITS')
const target = join(root, 'resources', 'runtime', 'tts')
if (!existsSync(source)) {
  throw new Error(`找不到 portable VITS 运行时：${source}\n请执行 npm run prepare:runtime -- "C:\\path\\to\\Tools\\VITS"`)
}
await mkdir(target, { recursive: true })
await cp(join(source, 'bin'), join(target, 'bin'), { recursive: true, force: true })
await cp(join(source, 'sherpa-onnx-vits-zh-ll'), join(target, 'sherpa-onnx-vits-zh-ll'), { recursive: true, force: true })
for (const path of [join(target, 'bin', 'sherpa-onnx-offline-tts.exe'), join(target, 'sherpa-onnx-vits-zh-ll', 'model.onnx')]) {
  const info = await stat(path); if (!info.size) throw new Error(`运行时文件无效：${path}`)
}
console.log(`已导入 portable 当前使用的唯一 TTS：${target}`)
