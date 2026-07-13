import type { AppSettings, Citation } from '../../shared/contracts'

export class LlmService {
  constructor(private readonly getSettings: () => AppSettings) {}
  async answer(question: string, citations: Citation[], history: Array<{ role: string; content: string }>): Promise<string> {
    const settings = this.getSettings()
    const provider=settings.providers.find(x=>x.id===settings.activeProviders.chat)
    const apiKey=provider?.apiKey||settings.llm.apiKey, model=provider?.chatModel||settings.llm.model, baseUrl=provider?.baseUrl||settings.llm.baseUrl
    if (!apiKey || !provider?.enabled) return localAnswer(citations)
    const context = citations.map((c, i) => `[${i + 1}] ${c.sourceLabel} · ${c.sourcePath}\n${c.text}`).join('\n\n')
    const activeWorld = ''
    const body = {
      model,
      messages: [
        { role: 'system', content: `${settings.systemPrompt}\n${settings.characterPrompt}${activeWorld}` },
        ...history.slice(-8).map((x) => ({ role: x.role, content: x.content })),
        { role: 'user', content: `用户问题：${question}\n\n知识库上下文：\n${context || '（没有召回到知识库内容）'}` }
      ],
      temperature: 0.25,
      extra_body: { enable_thinking: settings.llm.enableThinking }
    }
    if(provider.protocol==='anthropic') return this.anthropic(provider.baseUrl,apiKey,model,body.messages)
    if(provider.protocol==='gemini') return this.gemini(provider.baseUrl,apiKey,model,body.messages)
    const endpoint = `${baseUrl.replace(/\/$/, '')}/chat/completions`
    const controller = new AbortController(); const timeout = setTimeout(() => controller.abort(), 45000)
    try {
      const response = await fetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${settings.llm.apiKey}` }, body: JSON.stringify(body), signal: controller.signal })
      if (!response.ok) throw new Error(`LLM 请求失败 (${response.status})：${(await response.text()).slice(0, 300)}`)
      const json = await response.json() as { choices?: Array<{ message?: { content?: string } }> }
      const answer = json.choices?.[0]?.message?.content?.trim(); if (!answer) throw new Error('LLM 返回了空答案')
      return clean(answer)
    } finally { clearTimeout(timeout) }
  }
  private async anthropic(base:string,key:string,model:string,messages:Array<{role:string;content:string}>):Promise<string>{const system=messages.filter(x=>x.role==='system').map(x=>x.content).join('\n');const r=await fetch(`${base.replace(/\/$/,'')}/messages`,{method:'POST',headers:{'content-type':'application/json','x-api-key':key,'anthropic-version':'2023-06-01'},body:JSON.stringify({model,max_tokens:2048,system,messages:messages.filter(x=>x.role!=='system')})});if(!r.ok)throw new Error(`Anthropic 请求失败 (${r.status})：${(await r.text()).slice(0,300)}`);const j=await r.json() as {content?:Array<{text?:string}>};return clean(j.content?.map(x=>x.text||'').join('')||'')}
  private async gemini(base:string,key:string,model:string,messages:Array<{role:string;content:string}>):Promise<string>{const contents=messages.map(x=>({role:x.role==='assistant'?'model':'user',parts:[{text:`${x.role==='system'?'系统指令：':''}${x.content}`}]}));const r=await fetch(`${base.replace(/\/$/,'')}/models/${encodeURIComponent(model)}:generateContent?key=${encodeURIComponent(key)}`,{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({contents})});if(!r.ok)throw new Error(`Gemini 请求失败 (${r.status})：${(await r.text()).slice(0,300)}`);const j=await r.json() as {candidates?:Array<{content?:{parts?:Array<{text?:string}>}}>} ;return clean(j.candidates?.[0]?.content?.parts?.map(x=>x.text||'').join('')||'')}
}

function localAnswer(citations: Citation[]): string {
  if (!citations.length) return '当前没有可用的大模型配置，知识库中也没有找到相关依据。请联系管理员补充资料或配置 LLM。'
  const summary = citations.slice(0, 3).map((c, i) => `[${i + 1}] ${c.text.slice(0, 280)}`).join('\n\n')
  return `当前未配置 LLM，以下是本地知识库中最相关的内容：\n\n${summary}`
}
function clean(value: string): string { return value.replace(/^```(?:markdown|text)?/i, '').replace(/```$/, '').replace(/\*\*(.*?)\*\*/g, '$1').trim() }
