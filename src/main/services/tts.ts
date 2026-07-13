import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { existsSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import type { AppPaths } from './paths'
import type { AppSettings } from '../../shared/contracts'

export class TtsService {
  private process?: ChildProcessWithoutNullStreams
  constructor(private readonly paths: AppPaths, private readonly getSettings: () => AppSettings) {}
  status(): string {
    return existsSync(this.executable()) && existsSync(this.model()) ? '本地 VITS · sherpa-onnx-vits-zh-ll' : '缺少 VITS 运行时，请执行 npm run prepare:runtime'
  }
  async synthesize(text: string): Promise<Uint8Array> {
    const executable = this.executable(), model = this.model(), modelDir = join(model, '..')
    if (!existsSync(executable) || !existsSync(model)) throw new Error(this.status())
    await this.stop()
    const output = join(tmpdir(), `kbqa-tts-${process.pid}-${Date.now()}.wav`)
    const settings = this.getSettings(); const scale = (1 / Math.max(0.5, Math.min(2, settings.tts.speed))).toFixed(3)
    const args = ['--debug=0', `--vits-model=${model}`, `--vits-lexicon=${join(modelDir, 'lexicon.txt')}`, `--vits-tokens=${join(modelDir, 'tokens.txt')}`]
    const fsts = [join(modelDir, 'phone.fst'), join(modelDir, 'number.fst')]
    if (fsts.every(existsSync)) args.push(`--tts-rule-fsts=${fsts.join(',')}`)
    args.push(`--vits-length-scale=${scale}`, '--num-threads=4', `--sid=${settings.tts.voice}`, `--output-filename=${output}`, text.slice(0, 900))
    await new Promise<void>((resolve, reject) => {
      this.process = spawn(executable, args, { windowsHide: true })
      let stderr = ''; this.process.stderr.on('data', (chunk) => { stderr += String(chunk) })
      this.process.once('error', reject)
      this.process.once('exit', (code) => code === 0 && existsSync(output) ? resolve() : reject(new Error(`VITS 合成失败 (${code})：${stderr.slice(-500)}`)))
    })
    this.process = undefined
    const wav = readFileSync(output); rmSync(output, { force: true })
    return new Uint8Array(wav)
  }
  async stop(): Promise<void> { if (this.process && !this.process.killed) this.process.kill(); this.process = undefined }
  private executable(): string { return this.getSettings().tts.executablePath || join(this.paths.tts, 'bin', 'sherpa-onnx-offline-tts.exe') }
  private model(): string { return this.getSettings().tts.modelPath || join(this.paths.tts, 'sherpa-onnx-vits-zh-ll', 'model.onnx') }
}
