import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { basename, extname } from 'node:path'
import mammoth from 'mammoth'
import { marked } from 'marked'
import type { KnowledgeDatabase } from './database'
import type { LocalAiService } from './local-ai'

interface Section { text: string; label: string }

export class DocumentService {
  constructor(private readonly db: KnowledgeDatabase, private readonly ai: LocalAiService) {}
  async importFiles(paths: string[]): Promise<{ files: number; chunks: number }> {
    let files = 0, chunks = 0
    for (const path of paths) {
      const buffer = await readFile(path); const hash = sha(buffer)
      if (this.db.hasHash(hash)) continue
      const sections = await parse(path, buffer)
      const pieces = sections.flatMap((section) => chunk(section.text).map((text) => ({ text, label: section.label })))
      if (!pieces.length) continue
      const id = this.db.addDocument(path, basename(path), hash)
      for (let i = 0; i < pieces.length; i++) {
        const item = pieces[i]; const vector = await this.ai.embed(item.text)
        this.db.addChunk(id, i, item.text, path, item.label, sha(Buffer.from(item.text)), vector)
      }
      files++; chunks += pieces.length
    }
    return { files, chunks }
  }
}

async function parse(path: string, buffer: Buffer): Promise<Section[]> {
  const ext = extname(path).toLowerCase()
  if (ext === '.txt') return [{ text: buffer.toString('utf8'), label: '文本' }]
  if (ext === '.docx') return [{ text: (await mammoth.extractRawText({ buffer })).value, label: '文档' }]
  if (ext === '.md' || ext === '.markdown') return [{ text: stripHtml(await marked.parse(buffer.toString('utf8'))), label: 'Markdown' }]
  if (ext === '.html' || ext === '.htm') return [{ text: stripHtml(buffer.toString('utf8')), label: '网页' }]
  if (ext === '.pdf') {
    const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs')
    const pdf = await pdfjs.getDocument({ data: new Uint8Array(buffer), useWorkerFetch: false, isEvalSupported: false }).promise
    const sections: Section[] = []
    for (let page = 1; page <= pdf.numPages; page++) {
      const content = await (await pdf.getPage(page)).getTextContent()
      const text = content.items.map((item) => 'str' in item ? item.str : '').join(' ')
      if (text.trim()) sections.push({ text, label: `第 ${page} 页` })
    }
    return sections
  }
  throw new Error(`不支持的文档格式：${ext}`)
}

function chunk(input: string): string[] {
  const text = input.replace(/\s+/g, ' ').trim(); if (!text) return []
  const output: string[] = []; let start = 0
  while (start < text.length) {
    let end = Math.min(start + 1000, text.length)
    if (end < text.length) {
      const boundary = Math.max(...['。', '！', '？', '.', '!', '?'].map((char) => text.lastIndexOf(char, end)))
      if (boundary > start + 500) end = boundary + 1
    }
    output.push(text.slice(start, end).trim()); if (end === text.length) break; start = Math.max(start + 1, end - 140)
  }
  return output
}
function stripHtml(value: string): string { return value.replace(/<script[\s\S]*?<\/script>/gi, '').replace(/<style[\s\S]*?<\/style>/gi, '').replace(/<[^>]+>/g, ' ').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&') }
function sha(value: Buffer): string { return createHash('sha256').update(value).digest('hex') }
