namespace KnowledgeBaseQaAgent.Desktop.Models;

public enum AvatarState
{
    Idle,
    Listening,
    Thinking,
    Speaking,
    Error
}

public sealed class AppSettings
{
    public const string DefaultAdminPinHash = "8D969EEF6ECAD3C29A3A629280E686CF0C3F5D5A86AFF3CA12020C923ADC6C92";
    public const string DefaultPetImagePath = "";
    public const string DefaultLogoImagePath = "";
    public const string DefaultAssistantName = "通用知识库智能体";
    public const string DefaultSystemPrompt = "你是一个可用于组织、产品、项目与个人资料的通用知识库智能体。先判断用户输入是否需要查询知识库：寒暄、感谢、唤醒、询问使用方式等日常对话可直接简短回答；涉及文档事实、数据、规则、流程、项目、产品、服务或其他可核验信息时，根据给定知识库上下文和已触发的世界书设定作答。上下文不足时明确说明缺少依据，不猜测、不编造；只有使用知识库上下文时才在末尾列出引用编号。";
    public const string DefaultCharacterPrompt = "你是通用知识库智能体，保持专业、可靠且友好。根据用户配置的知识库和角色设定回答，不假定任何组织或特定行业背景。";
    public static readonly string[] DefaultQuickQuestions =
    [
        "请概括知识库的主要内容。",
        "有哪些重要信息需要优先了解？",
        "请列出相关流程或操作步骤。",
        "有哪些规则、限制或注意事项？",
        "请整理相关项目、产品或服务清单。",
        "知识库中有哪些常见问题？",
        "请比较文档中的不同方案。",
        "当前问题缺少哪些信息？"
    ];

    public string ChatProviderId { get; set; } = "openai-chat";
    public string EmbeddingProviderId { get; set; } = "local-hash-embedding";
    public string SpeechRecognizerId { get; set; } = "windows-dictation";
    public string SpeechSynthesizerId { get; set; } = "local-vits-tts";
    public string AdminPinHash { get; set; } = DefaultAdminPinHash;
    public string AssistantName { get; set; } = DefaultAssistantName;
    public string VisitorWindowTitle { get; set; } = DefaultAssistantName;
    public string VisitorHeadline { get; set; } = $"您好，我是{DefaultAssistantName}";
    public string GreetingText { get; set; } = "您好，我可以根据已导入的知识库回答问题，也支持文字和语音交互。";
    public string WakeHintText { get; set; } = "可点击桌宠，或说“助手”唤醒。";
    public string PetHintText { get; set; } = "点我咨询";
    public string AdminButtonText { get; set; } = "管理员";
    public string CloseButtonText { get; set; } = "关闭";
    public string EndSessionButtonText { get; set; } = "结束本次咨询";
    public string QuickQuestionsHeader { get; set; } = "常见问题";
    public string ConversationHeader { get; set; } = "问答记录";
    public string SendButtonText { get; set; } = "发送";
    public string VoiceButtonText { get; set; } = "语音提问";
    public string IdleStatusText { get; set; } = "待命";
    public string ListeningStatusText { get; set; } = "聆听";
    public string ThinkingStatusText { get; set; } = "思考";
    public string SpeakingStatusText { get; set; } = "说话";
    public string ErrorStatusText { get; set; } = "异常";
    public string PetImagePath { get; set; } = DefaultPetImagePath;
    public string PetFramesDirectory { get; set; } = "";
    public int PetFrameIntervalMs { get; set; } = 650;
    public string LogoImagePath { get; set; } = DefaultLogoImagePath;
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;
    public string CharacterPrompt { get; set; } = DefaultCharacterPrompt;
    public int RetrievalTopK { get; set; } = 12;
    public List<string> AssistantTags { get; set; } =
    [
        "知识库问答",
        "语音交互",
        "触屏友好",
        "本地索引"
    ];
    public List<string> WakeWords { get; set; } =
    [
        "助手",
        "小助手",
        "你好助手",
        "智能助手"
    ];
    public List<string> QuickQuestions { get; set; } = [.. DefaultQuickQuestions];
    public List<ProviderConfig> Providers { get; set; } = ProviderDefaults.Create();
    public List<WorldBookEntry> WorldBookEntries { get; set; } =
    [
        new()
        {
            Name = "事实核验",
            Keywords = ["依据", "来源", "文档", "规则", "流程"],
            Content = "回答可核验的事实、规则或流程时优先依据知识库；如果知识库没有明确内容，不要臆测，并说明还需要哪些资料。"
        }
    ];
}

public sealed class WorldBookEntry
{
    public string Name { get; set; } = "";
    public List<string> Keywords { get; set; } = [];
    public string Content { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed record PromptProfile(
    string SystemPrompt,
    string CharacterPrompt,
    IReadOnlyList<WorldBookEntry> ActiveWorldBookEntries);

public sealed class ProviderConfig
{
    public string ProviderId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string AuthRef { get; set; } = "";
    public Dictionary<string, string> Options { get; set; } = new();
}

public static class ProviderDefaults
{
    public static List<ProviderConfig> Create() =>
    [
        new()
        {
            ProviderId = "openai-chat",
            DisplayName = "阿里通义 / 百炼 Chat",
            Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions",
            Model = "qwen-plus",
            AuthRef = "llm-aliyun",
            Options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["llmProviderCode"] = "aliyun",
                ["baseUrl"] = "https://dashscope.aliyuncs.com/compatible-mode/v1",
                ["chatPath"] = "/v1/chat/completions",
                ["chatMethod"] = "POST",
                ["modelsPath"] = "/v1/models",
                ["modelsMethod"] = "GET",
                ["modelsMode"] = "auto",
                ["apiKeyHeader"] = "Authorization",
                ["apiKeyPrefix"] = "Bearer ",
                ["enableThinking"] = "false"
            }
        },
        new()
        {
            ProviderId = "openai-embedding",
            DisplayName = "OpenAI-compatible Embedding",
            Endpoint = "https://api.openai.com/v1/embeddings",
            Model = "text-embedding-3-small",
            AuthRef = "openai-compatible"
        },
        new()
        {
            ProviderId = "local-hash-embedding",
            DisplayName = "Local Hash Embedding",
            Model = "local-hash-384"
        },
        new()
        {
            ProviderId = "local-context-chat",
            DisplayName = "Local Context Summary",
            Model = "local-context"
        },
        new()
        {
            ProviderId = "windows-dictation",
            DisplayName = "Windows Dictation",
            Model = "system-speech"
        },
        new()
        {
            ProviderId = "windows-tts",
            DisplayName = "Windows TTS",
            Model = "system-speech"
        },
        new()
        {
            ProviderId = "local-vits-tts",
            DisplayName = "本地 VITS 中文动漫音色 (sherpa-onnx)",
            Endpoint = "Tools\\VITS\\bin\\sherpa-onnx-offline-tts.exe",
            Model = "Tools\\VITS\\vits-melo-tts-zh_en\\model.onnx",
            Options = new Dictionary<string, string>
            {
                ["arguments"] = "--debug=0 --vits-model=\"{model}\" --vits-lexicon=\"{modelDir}\\lexicon.txt\" --vits-tokens=\"{modelDir}\\tokens.txt\" --tts-rule-fsts=\"{modelDir}\\phone.fst,{modelDir}\\number.fst\" --vits-length-scale=1 --num-threads=4 --sid={voice} --output-filename=\"{output}\" \"{text}\"",
                ["inputMode"] = "argument",
                ["speed"] = "1",
                ["voice"] = "0",
                ["help"] = "VITS ONNX via sherpa-onnx. Model is a VITS .onnx file under Tools\\VITS; Voice is speaker id (sid)."
            }
        },
        new()
        {
            ProviderId = "local-command-tts",
            DisplayName = "Kokoro Local ONNX TTS (sherpa)",
            Endpoint = "Tools\\Kokoro\\bin\\sherpa-onnx-offline-tts.exe",
            Model = "Tools\\Kokoro\\kokoro-multi-lang-v1_1\\model.onnx",
            Options = new Dictionary<string, string>
            {
                ["arguments"] = "--debug=0 --kokoro-model=\"{model}\" --kokoro-voices=\"{modelDir}\\voices.bin\" --kokoro-tokens=\"{modelDir}\\tokens.txt\" --kokoro-data-dir=\"{modelDir}\\espeak-ng-data\" --kokoro-lexicon=\"{modelDir}\\lexicon-us-en.txt,{modelDir}\\lexicon-zh.txt\" --tts-rule-fsts=\"{modelDir}\\date-zh.fst,{modelDir}\\phone-zh.fst,{modelDir}\\number-zh.fst\" --kokoro-length-scale=1 --tts-silence-scale=0.15 --num-threads=4 --sid={voice} --output-filename=\"{output}\" \"{text}\"",
                ["inputMode"] = "argument",
                ["singlePassMaxChars"] = "220",
                ["speed"] = "1",
                ["voice"] = "3",
                ["help"] = "Kokoro ONNX via sherpa-onnx. Use official kokoro-multi-lang-v1_1/model.onnx. v1.1 has 103 speakers; Chinese female voices are 3-57 and Chinese male voices are 58-102. Tokens: {model}, {modelDir}, {voice}, {output}, {text}, {textFile}."
            }
        },
        new()
        {
            ProviderId = "openai-asr",
            DisplayName = "OpenAI-compatible ASR",
            Endpoint = "https://api.openai.com/v1/audio/transcriptions",
            Model = "whisper-1",
            AuthRef = "openai-compatible"
        },
        new()
        {
            ProviderId = "openai-tts",
            DisplayName = "OpenAI-compatible TTS",
            Endpoint = "https://api.openai.com/v1/audio/speech",
            Model = "gpt-4o-mini-tts",
            AuthRef = "openai-compatible",
            Options = new Dictionary<string, string> { ["voice"] = "alloy" }
        },
        new()
        {
            ProviderId = "aliyun-qwen-tts",
            DisplayName = "阿里百炼 Qwen TTS",
            Endpoint = "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation",
            Model = "qwen3-tts-instruct-flash",
            AuthRef = "llm-aliyun",
            Options = new Dictionary<string, string>
            {
                ["voice"] = "Cherry",
                ["language_type"] = "Chinese",
                ["instructions"] = "温柔、清晰、亲切，语速适中，适合触屏咨询场景。",
                ["optimize_instructions"] = "true",
                ["realtimeEndpoint"] = "wss://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime?model={model}"
            }
        },
        new()
        {
            ProviderId = "whisper-local",
            DisplayName = "Whisper.net Local ASR",
            Model = "ggml-base.bin",
            Options = new Dictionary<string, string> { ["modelPath"] = "" }
        }
    ];
}

public sealed record SourceDocument(
    long Id,
    string Path,
    string Title,
    string ContentHash,
    DateTimeOffset CreatedAt);

public sealed record KnowledgeChunk(
    long Id,
    long DocumentId,
    int Ordinal,
    string Text,
    string SourcePath,
    string SourceLabel,
    string ContentHash,
    DateTimeOffset CreatedAt,
    float[] Embedding);

public sealed record SearchResult(KnowledgeChunk Chunk, double Score);

public sealed record ChatMessage(
    long Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    string ProviderId,
    string Model,
    string? CitationChunkIds);

public sealed record ParsedDocument(string Path, string Title, IReadOnlyList<ParsedSection> Sections);

public sealed record ParsedSection(string Text, string SourceLabel);

public sealed record TextChunk(string Text, string SourceLabel, int Ordinal);

public sealed record RagAnswer(string Answer, IReadOnlyList<SearchResult> Citations);

public sealed record LogEntry(DateTimeOffset Timestamp, string Level, string Message);
