import { createCipheriv, createDecipheriv, randomBytes } from 'node:crypto'
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import type { AppPaths } from './paths'

interface SecretValue { nonce: string; ciphertext: string; tag: string }
interface Container { version: 'portable-secrets-v1'; secrets: Record<string, SecretValue> }

export class SecretStore {
  private readonly keyPath: string
  constructor(private readonly paths: AppPaths) { this.keyPath = `${paths.root}/secret.key` }
  get(...names: string[]): string {
    const container = this.load()
    for (const name of names) {
      const value = container.secrets[name]
      if (!value) continue
      try {
        const decipher = createDecipheriv('aes-256-gcm', this.key(), Buffer.from(value.nonce, 'base64'))
        decipher.setAuthTag(Buffer.from(value.tag, 'base64'))
        return Buffer.concat([decipher.update(Buffer.from(value.ciphertext, 'base64')), decipher.final()]).toString('utf8')
      } catch { /* invalid legacy entry is ignored */ }
    }
    return ''
  }
  set(name: string, secret: string): void {
    const container = this.load()
    if (!secret) delete container.secrets[name]
    else {
      const nonce = randomBytes(12); const cipher = createCipheriv('aes-256-gcm', this.key(), nonce)
      const ciphertext = Buffer.concat([cipher.update(secret, 'utf8'), cipher.final()])
      container.secrets[name] = { nonce: nonce.toString('base64'), ciphertext: ciphertext.toString('base64'), tag: cipher.getAuthTag().toString('base64') }
    }
    writeFileSync(this.paths.secrets, JSON.stringify(container, null, 2), 'utf8')
  }
  private load(): Container {
    if (!existsSync(this.paths.secrets)) return { version: 'portable-secrets-v1', secrets: {} }
    try {
      const parsed = JSON.parse(readFileSync(this.paths.secrets, 'utf8')) as Container
      return parsed.version === 'portable-secrets-v1' && parsed.secrets ? parsed : { version: 'portable-secrets-v1', secrets: {} }
    } catch { return { version: 'portable-secrets-v1', secrets: {} } }
  }
  private key(): Buffer {
    if (existsSync(this.keyPath)) return Buffer.from(readFileSync(this.keyPath, 'utf8').trim(), 'base64')
    const key = randomBytes(32); writeFileSync(this.keyPath, key.toString('base64'), 'utf8'); return key
  }
}
