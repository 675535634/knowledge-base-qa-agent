import type { AskResult, AppSettings } from '../../shared/contracts'
import type { KnowledgeDatabase } from './database'
import type { LocalAiService } from './local-ai'
import type { LlmService } from './llm'

export class RagService {
  constructor(private readonly db: KnowledgeDatabase, private readonly ai: LocalAiService, private readonly llm: LlmService, private readonly getSettings: () => AppSettings) {}
  async ask(question: string): Promise<AskResult> {
    const text = question.trim(); if (!text) throw new Error('请输入问题')
    const settings = this.getSettings(); const vector = await this.ai.embed(text)
    await this.db.ensureEmbeddingIndex(this.ai.embeddingSignature(), (value) => this.ai.embed(value))
    const broad = /(所有|全部|完整|一共|汇总|列表|清单|有哪些|哪些).*(专业|课程|项目|业务|材料|流程|部门|窗口|费用|政策|服务)/.test(text)
    const citations = this.db.search(vector, broad ? Math.max(20, settings.retrievalTopK) : settings.retrievalTopK).filter((x) => x.score > -0.5)
    const history = this.db.history(12).map((x) => ({ role: x.role, content: x.content }))
    this.db.addMessage('user', text, 'user', '')
    let answer: string
    try { answer = await this.llm.answer(text, citations, history) }
    catch (error) { answer = `云端模型暂时不可用。${error instanceof Error ? error.message : String(error)}\n\n${citations.length ? '已找到本地知识库相关内容，请稍后重试。' : '本地知识库也没有找到相关依据。'}` }
    this.db.addMessage('assistant', answer, this.getSettings().llm.apiKey ? 'openai-compatible' : 'local-context', settings.llm.model, citations.map((x) => x.id))
    return { answer, citations }
  }
}
