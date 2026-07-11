using System.Globalization;
using System.Text.Json;
using KnowledgeBaseQaAgent.Desktop.Models;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class SettingsService
{
    private const string LegacySystemPrompt =
        "你是一个基于本地知识库回答问题的触屏问答助手。只根据给定知识库上下文和已触发的世界书设定作答；如果上下文不足，明确说明缺少依据。回答要简洁、礼貌，适合现场游客理解。回答末尾列出引用编号。";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.SettingsPath))
        {
            var defaults = new AppSettings();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        AppSettings? settings;
        await using (var stream = File.OpenRead(_paths.SettingsPath))
        {
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        }

        var normalized = Normalize(settings ?? new AppSettings());
        await SaveAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        await using var stream = File.Create(_paths.SettingsPath);
        await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var existing = settings.Providers.ToDictionary(x => x.ProviderId, StringComparer.OrdinalIgnoreCase);
        foreach (var provider in ProviderDefaults.Create())
        {
            if (!existing.ContainsKey(provider.ProviderId))
            {
                settings.Providers.Add(provider);
            }
        }

        settings.Providers.RemoveAll(provider =>
            provider.ProviderId.Equals("piper-local-tts", StringComparison.OrdinalIgnoreCase));
        if (settings.SpeechSynthesizerId.Equals("piper-local-tts", StringComparison.OrdinalIgnoreCase))
        {
            settings.SpeechSynthesizerId = "local-vits-tts";
        }

        if (settings.RetrievalTopK <= 0)
        {
            settings.RetrievalTopK = 12;
        }

        if (string.IsNullOrWhiteSpace(settings.AdminPinHash))
        {
            settings.AdminPinHash = AppSettings.DefaultAdminPinHash;
        }

        if (string.IsNullOrWhiteSpace(settings.GreetingText))
        {
            settings.GreetingText = "您好，请问有什么需要了解的？可以点击下面的问题，也可以直接语音提问。";
        }

        settings.AssistantName = DefaultIfEmptyOrOldDefault(settings.AssistantName, AppSettings.DefaultAssistantName);
        settings.VisitorWindowTitle = DefaultIfEmpty(settings.VisitorWindowTitle, settings.AssistantName);
        if (settings.VisitorWindowTitle == "智能问答助手")
        {
            settings.VisitorWindowTitle = settings.AssistantName;
        }

        settings.VisitorHeadline = DefaultIfEmpty(settings.VisitorHeadline, $"您好，我是{settings.AssistantName}");
        if (settings.VisitorHeadline == "您好，我是智能问答助手")
        {
            settings.VisitorHeadline = $"您好，我是{settings.AssistantName}";
        }

        settings.WakeHintText = DefaultIfEmpty(settings.WakeHintText, "可点击桌宠，或说“助手”唤醒。");
        settings.PetHintText = DefaultIfEmpty(settings.PetHintText, "点我咨询");
        settings.AdminButtonText = DefaultIfEmpty(settings.AdminButtonText, "管理员");
        settings.CloseButtonText = DefaultIfEmpty(settings.CloseButtonText, "关闭");
        settings.EndSessionButtonText = DefaultIfEmpty(settings.EndSessionButtonText, "结束本次咨询");
        settings.QuickQuestionsHeader = DefaultIfEmpty(settings.QuickQuestionsHeader, "常见问题");
        settings.ConversationHeader = DefaultIfEmpty(settings.ConversationHeader, "问答记录");
        settings.SendButtonText = DefaultIfEmpty(settings.SendButtonText, "发送");
        settings.VoiceButtonText = DefaultIfEmpty(settings.VoiceButtonText, "语音提问");
        settings.IdleStatusText = DefaultIfEmpty(settings.IdleStatusText, "待命");
        settings.ListeningStatusText = DefaultIfEmpty(settings.ListeningStatusText, "聆听");
        settings.ThinkingStatusText = DefaultIfEmpty(settings.ThinkingStatusText, "思考");
        settings.SpeakingStatusText = DefaultIfEmpty(settings.SpeakingStatusText, "说话");
        settings.ErrorStatusText = DefaultIfEmpty(settings.ErrorStatusText, "异常");
        if (settings.PetFrameIntervalMs < 120)
        {
            settings.PetFrameIntervalMs = 650;
        }

        if (string.IsNullOrWhiteSpace(settings.PetImagePath) && string.IsNullOrWhiteSpace(settings.PetFramesDirectory))
        {
            settings.PetImagePath = AppSettings.DefaultPetImagePath;
        }

        if (string.IsNullOrWhiteSpace(settings.LogoImagePath))
        {
            settings.LogoImagePath = AppSettings.DefaultLogoImagePath;
        }

        settings.SystemPrompt = DefaultIfEmptyOrOldSystemPrompt(settings.SystemPrompt, AppSettings.DefaultSystemPrompt);
        settings.CharacterPrompt = DefaultIfEmpty(settings.CharacterPrompt, AppSettings.DefaultCharacterPrompt);
        settings.WorldBookEntries = settings.WorldBookEntries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Content))
            .Select(entry =>
            {
                entry.Name = DefaultIfEmpty(entry.Name, "未命名世界书");
                entry.Keywords = entry.Keywords
                    .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                    .Select(keyword => keyword.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(16)
                    .ToList();
                return entry;
            })
            .Take(50)
            .ToList();

        if (settings.WakeWords.Count == 0)
        {
            settings.WakeWords.AddRange(["助手", "小助手", "你好助手", "智能助手"]);
        }

        if (settings.AssistantTags.Count == 0)
        {
            settings.AssistantTags.AddRange(["知识库问答", "语音交互", "触屏友好", "本地索引"]);
        }

        if (settings.QuickQuestions.Count == 0)
        {
            settings.QuickQuestions.AddRange(
            [
                "这里可以办理什么业务？",
                "开放时间是什么？",
                "我应该去哪个窗口？",
                "附近有什么服务设施？"
            ]);
        }

        RepairAliyunQwenTtsSelection(settings);
        RepairAliyunQwenAsrSelection(settings);
        RepairLocalVitsTtsSelection(settings);
        RepairKokoroLocalTtsSelection(settings);
        return settings;
    }

    private static void RepairLocalVitsTtsSelection(AppSettings settings)
    {
        var vits = settings.Providers.FirstOrDefault(provider =>
            provider.ProviderId.Equals("local-vits-tts", StringComparison.OrdinalIgnoreCase));
        if (vits is null)
        {
            return;
        }

        var model = vits.Model.Trim();
        var modelPath = Path.IsPathRooted(model)
            ? model
            : Path.Combine(AppContext.BaseDirectory, model);
        if (string.IsNullOrWhiteSpace(model) ||
            model.Contains("vits-zh-hf-theresa", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("vits-zh-hf-eula", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(modelPath))
        {
            vits.DisplayName = "本地 VITS 中文动漫音色 (sherpa-onnx)";
            vits.Endpoint = "Tools\\VITS\\bin\\sherpa-onnx-offline-tts.exe";
            vits.Model = "Tools\\VITS\\vits-melo-tts-zh_en\\model.onnx";
            vits.AuthRef = "";
            vits.Options["voice"] = "0";
            vits.Options["inputMode"] = "argument";
            vits.Options["speed"] = DefaultIfEmpty(vits.Options.GetValueOrDefault("speed", ""), "1");
        }

        vits.Endpoint = DefaultIfEmpty(vits.Endpoint, "Tools\\VITS\\bin\\sherpa-onnx-offline-tts.exe");
        vits.Options["inputMode"] = "argument";
        vits.Options["speed"] = DefaultIfEmpty(vits.Options.GetValueOrDefault("speed", ""), "1");
        if (vits.Model.Contains("vits-melo-tts-zh_en", StringComparison.OrdinalIgnoreCase) ||
            vits.Model.Contains("breeze2-vits-onnx", StringComparison.OrdinalIgnoreCase))
        {
            vits.Options["voice"] = "0";
        }
        else
        {
            vits.Options["voice"] = DefaultIfEmpty(vits.Options.GetValueOrDefault("voice", ""), "0");
        }

        vits.Options["singlePassMaxChars"] = GetLocalVitsSinglePassMaxChars(vits.Model);
        vits.Options["arguments"] = BuildLocalVitsArgumentsForModel(vits.Model, vits.Options["speed"]);
    }

    private static string GetLocalVitsSinglePassMaxChars(string model) =>
        model.Contains("sherpa-onnx-vits-zh-ll", StringComparison.OrdinalIgnoreCase) ? "70" : "140";

    private static string BuildLocalVitsArgumentsForModel(string model, string speed)
    {
        var lengthScale = "1";
        if (double.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSpeed) &&
            parsedSpeed > 0)
        {
            lengthScale = (1.0 / Math.Clamp(parsedSpeed, 0.5, 2.0)).ToString("0.###", CultureInfo.InvariantCulture);
        }

        var modelPath = Path.IsPathRooted(model)
            ? model
            : Path.Combine(AppContext.BaseDirectory, model);
        var modelDirectory = Path.GetDirectoryName(modelPath);
        var hasRuleFsts = !string.IsNullOrWhiteSpace(modelDirectory) &&
            File.Exists(Path.Combine(modelDirectory, "phone.fst")) &&
            File.Exists(Path.Combine(modelDirectory, "number.fst"));
        var ruleFstsArgument = hasRuleFsts
            ? " --tts-rule-fsts=\"{modelDir}\\phone.fst,{modelDir}\\number.fst\""
            : "";
        return $"--debug=0 --vits-model=\"{{model}}\" --vits-lexicon=\"{{modelDir}}\\lexicon.txt\" --vits-tokens=\"{{modelDir}}\\tokens.txt\"{ruleFstsArgument} --vits-length-scale={lengthScale} --num-threads=4 --sid={{voice}} --output-filename=\"{{output}}\" \"{{text}}\"";
    }

    private static void RepairKokoroLocalTtsSelection(AppSettings settings)
    {
        var kokoro = settings.Providers.FirstOrDefault(provider =>
            provider.ProviderId.Equals("local-command-tts", StringComparison.OrdinalIgnoreCase));
        if (kokoro is null)
        {
            return;
        }

        var looksLikeKokoro = kokoro.DisplayName.Contains("Kokoro", StringComparison.OrdinalIgnoreCase) ||
            kokoro.Endpoint.Contains("Kokoro", StringComparison.OrdinalIgnoreCase) ||
            kokoro.Model.Contains("kokoro", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeKokoro)
        {
            return;
        }

        kokoro.DisplayName = $"Kokoro Local ONNX TTS (sherpa, sid {DefaultIfEmpty(kokoro.Options.GetValueOrDefault("voice", ""), "3")})";
        kokoro.Endpoint = "Tools\\Kokoro\\bin\\sherpa-onnx-offline-tts.exe";
        kokoro.Model = "Tools\\Kokoro\\kokoro-multi-lang-v1_1\\model.onnx";
        kokoro.AuthRef = "";
        kokoro.Options["voice"] = DefaultIfEmpty(kokoro.Options.GetValueOrDefault("voice", ""), "3");
        kokoro.Options["inputMode"] = "argument";
        kokoro.Options["singlePassMaxChars"] = "220";
        kokoro.Options["speed"] = DefaultIfEmpty(kokoro.Options.GetValueOrDefault("speed", ""), "1");
        kokoro.Options["arguments"] = "--debug=0 --kokoro-model=\"{model}\" --kokoro-voices=\"{modelDir}\\voices.bin\" --kokoro-tokens=\"{modelDir}\\tokens.txt\" --kokoro-data-dir=\"{modelDir}\\espeak-ng-data\" --kokoro-lexicon=\"{modelDir}\\lexicon-us-en.txt,{modelDir}\\lexicon-zh.txt\" --tts-rule-fsts=\"{modelDir}\\date-zh.fst,{modelDir}\\phone-zh.fst,{modelDir}\\number-zh.fst\" --kokoro-length-scale=1 --tts-silence-scale=0.15 --num-threads=4 --sid={voice} --output-filename=\"{output}\" \"{text}\"";
        kokoro.Options["help"] = "Kokoro ONNX via sherpa-onnx. Use official kokoro-multi-lang-v1_1/model.onnx. v1.1 has 103 speakers; Chinese female voices are 3-57 and Chinese male voices are 58-102.";
    }

    private static void RepairAliyunQwenAsrSelection(AppSettings settings)
    {
        var asr = settings.Providers.FirstOrDefault(provider =>
            provider.ProviderId.Equals("openai-asr", StringComparison.OrdinalIgnoreCase));
        if (asr is null ||
            !asr.Model.Contains("qwen3-asr", StringComparison.OrdinalIgnoreCase) ||
            !asr.Model.Contains("realtime", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var llmProviderCode = asr.Options.GetValueOrDefault("llmProviderCode", "");
        if (!llmProviderCode.Equals("aliyun", StringComparison.OrdinalIgnoreCase) &&
            !asr.AuthRef.Equals("llm-aliyun", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (asr.Endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) &&
            asr.Options.ContainsKey("providerMode"))
        {
            return;
        }

        var workspaceId = asr.Options.GetValueOrDefault("workspaceId", "").Trim();
        var aliyunQwenTts = settings.Providers.FirstOrDefault(provider =>
            provider.ProviderId.Equals("aliyun-qwen-tts", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(workspaceId) && aliyunQwenTts is not null)
        {
            workspaceId = aliyunQwenTts.Options.GetValueOrDefault("workspaceId", "").Trim();
        }

        if (string.IsNullOrWhiteSpace(workspaceId) && aliyunQwenTts is not null)
        {
            workspaceId = ExtractAliyunWorkspaceId(aliyunQwenTts.Options.GetValueOrDefault("realtimeEndpoint", aliyunQwenTts.Endpoint));
        }

        asr.DisplayName = "阿里通义 / 百炼 ASR";
        asr.AuthRef = "llm-aliyun";
        asr.Options["baseUrl"] = "https://dashscope.aliyuncs.com/compatible-mode/v1";
        asr.Options["path"] = "/api-ws/v1/realtime";
        asr.Options["llmProviderCode"] = "aliyun";
        asr.Options["providerMode"] = "aliyun-qwen-realtime";
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            asr.Endpoint = $"wss://{workspaceId}.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime?model={Uri.EscapeDataString(asr.Model)}";
            asr.Options["workspaceId"] = workspaceId;
        }
        else
        {
            asr.Endpoint = "wss://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime?model={model}";
        }
    }

    private static void RepairAliyunQwenTtsSelection(AppSettings settings)
    {
        var openAiTts = settings.Providers.FirstOrDefault(provider =>
            provider.ProviderId.Equals("openai-tts", StringComparison.OrdinalIgnoreCase));
        var aliyunQwenTts = settings.Providers.FirstOrDefault(provider =>
            provider.ProviderId.Equals("aliyun-qwen-tts", StringComparison.OrdinalIgnoreCase));
        if (openAiTts is null || aliyunQwenTts is null ||
            !settings.SpeechSynthesizerId.Equals("openai-tts", StringComparison.OrdinalIgnoreCase) ||
            !LooksLikeAliyunQwenTtsOnOpenAiProvider(openAiTts))
        {
            return;
        }

        settings.SpeechSynthesizerId = "aliyun-qwen-tts";
        aliyunQwenTts.DisplayName = "阿里百炼 Qwen TTS";
        aliyunQwenTts.Model = DefaultIfEmpty(openAiTts.Model, "qwen3-tts-instruct-flash");
        aliyunQwenTts.AuthRef = "llm-aliyun";
        aliyunQwenTts.Options["voice"] = NormalizeAliyunTtsVoice(
            openAiTts.Options.GetValueOrDefault("voice", aliyunQwenTts.Options.GetValueOrDefault("voice", "Cherry")));
        aliyunQwenTts.Options["language_type"] = DefaultIfEmpty(aliyunQwenTts.Options.GetValueOrDefault("language_type", ""), "Chinese");
        aliyunQwenTts.Options["instructions"] = DefaultIfEmpty(
            aliyunQwenTts.Options.GetValueOrDefault("instructions", ""),
            "温柔、清晰、亲切，语速适中，适合触屏咨询场景。");
        aliyunQwenTts.Options["optimize_instructions"] = DefaultIfEmpty(
            aliyunQwenTts.Options.GetValueOrDefault("optimize_instructions", ""),
            "true");

        if (aliyunQwenTts.Model.Contains("realtime", StringComparison.OrdinalIgnoreCase))
        {
            aliyunQwenTts.Endpoint = DefaultIfEmpty(
                aliyunQwenTts.Options.GetValueOrDefault("realtimeEndpoint", ""),
                "wss://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime?model={model}");
        }
        else
        {
            aliyunQwenTts.Endpoint = "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
        }

        aliyunQwenTts.Options["realtimeEndpoint"] = aliyunQwenTts.Endpoint;
    }

    private static bool LooksLikeAliyunQwenTtsOnOpenAiProvider(ProviderConfig provider) =>
        provider.Model.Contains("qwen3-tts", StringComparison.OrdinalIgnoreCase) ||
        (provider.Endpoint.Contains("dashscope.aliyuncs.com/compatible-mode", StringComparison.OrdinalIgnoreCase) &&
            provider.Endpoint.Contains("/audio/speech", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeAliyunTtsVoice(string voice)
    {
        var value = (voice ?? "").Trim();
        return value.Equals("alloy", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("echo", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("nova", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(value)
            ? "Cherry"
            : value;
    }

    private static string ExtractAliyunWorkspaceId(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return "";
        }

        const string suffix = ".cn-beijing.maas.aliyuncs.com";
        return uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? uri.Host[..^suffix.Length]
            : "";
    }

    private static string DefaultIfEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string DefaultIfEmptyOrOldDefault(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || value == "智能问答助手" ? fallback : value;

    private static string DefaultIfEmptyOrOldSystemPrompt(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || value.Trim() == LegacySystemPrompt ? fallback : value;
}
