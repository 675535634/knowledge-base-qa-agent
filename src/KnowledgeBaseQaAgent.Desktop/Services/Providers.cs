using System.Globalization;
using System.Media;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using KnowledgeBaseQaAgent.Desktop.Models;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class ProviderRegistry
{
    private readonly SettingsService _settingsService;
    private readonly ICredentialService _credentialService;
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient = new();

    public ProviderRegistry(SettingsService settingsService, ICredentialService credentialService, AppSettings settings)
    {
        _settingsService = settingsService;
        _credentialService = credentialService;
        _settings = settings;
    }

    public IReadOnlyList<ProviderConfig> Providers => _settings.Providers;

    public ProviderConfig GetConfig(string providerId) =>
        _settings.Providers.FirstOrDefault(provider => provider.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Provider not configured: {providerId}");

    public IChatProvider CreateChatProvider() =>
        _settings.ChatProviderId switch
        {
            "local-context-chat" => new LocalContextChatProvider(GetConfig("local-context-chat")),
            _ => new OpenAiCompatibleChatProvider(GetConfig(_settings.ChatProviderId), _credentialService, _httpClient)
        };

    public IEmbeddingProvider CreateEmbeddingProvider() =>
        _settings.EmbeddingProviderId switch
        {
            "local-hash-embedding" => new LocalHashEmbeddingProvider(),
            _ => new OpenAiCompatibleEmbeddingProvider(GetConfig(_settings.EmbeddingProviderId), _credentialService, _httpClient)
        };

    public ISpeechRecognizer CreateSpeechRecognizer() =>
        _settings.SpeechRecognizerId switch
        {
            "windows-dictation" => new WindowsDictationRecognizer(),
            "openai-asr" => new MicrophoneAudioTranscriptionRecognizer(CreateAudioTranscriber()),
            "whisper-local" => new WhisperLocalRecognizer(GetConfig("whisper-local")),
            _ => new UnsupportedSpeechRecognizer(_settings.SpeechRecognizerId, "Cloud ASR is configured for file transcription, not live microphone capture in the MVP.")
        };

    public IAudioTranscriber CreateAudioTranscriber()
    {
        if (!_settings.SpeechRecognizerId.Equals("openai-asr", StringComparison.OrdinalIgnoreCase))
        {
            return new UnsupportedAudioTranscriber(_settings.SpeechRecognizerId, "当前 ASR Provider 不支持音频文件测试。请选择 OpenAI-compatible ASR / 阿里百炼 ASR 后再测试。");
        }

        var config = GetConfig("openai-asr");
        return LooksLikeAliyunRealtimeAsr(config)
            ? new AliyunQwenRealtimeAudioTranscriber(config, _credentialService)
            : new OpenAiCompatibleAudioTranscriber(config, _credentialService, _httpClient);
    }

    private static bool LooksLikeAliyunRealtimeAsr(ProviderConfig config) =>
        config.Model.Contains("qwen3-asr", StringComparison.OrdinalIgnoreCase) &&
        config.Model.Contains("realtime", StringComparison.OrdinalIgnoreCase) ||
        config.Endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase);

    public ISpeechSynthesizer CreateSpeechSynthesizer() =>
        _settings.SpeechSynthesizerId switch
        {
            "windows-tts" => new WindowsTextToSpeech(),
            "local-vits-tts" => new ExternalLocalSpeechSynthesizer(GetConfig("local-vits-tts")),
            "local-command-tts" => new ExternalLocalSpeechSynthesizer(GetConfig("local-command-tts")),
            "openai-tts" => new OpenAiCompatibleSpeechSynthesizer(GetConfig("openai-tts"), _credentialService, _httpClient),
            "aliyun-qwen-tts" => new AliyunQwenSpeechSynthesizer(GetConfig("aliyun-qwen-tts"), _credentialService, _httpClient),
            _ => new WindowsTextToSpeech()
        };

    public PromptProfile CreatePromptProfile(string question)
    {
        var activeWorldBookEntries = _settings.WorldBookEntries
            .Where(entry => entry.Enabled)
            .Where(entry => EntryMatches(entry, question))
            .Take(8)
            .ToArray();
        return new PromptProfile(
            _settings.SystemPrompt,
            _settings.CharacterPrompt,
            activeWorldBookEntries);
    }

    public async Task SaveSettingsAsync(CancellationToken cancellationToken = default) =>
        await _settingsService.SaveAsync(_settings, cancellationToken);

    private static bool EntryMatches(WorldBookEntry entry, string question)
    {
        if (entry.Keywords.Count == 0)
        {
            return true;
        }

        return entry.Keywords.Any(keyword =>
            question.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class LocalHashEmbeddingProvider : IEmbeddingProvider
{
    public string ProviderId => "local-hash-embedding";
    public int Dimensions => 384;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vector = new float[Dimensions];
        foreach (Match match in Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}_]+"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddToken(vector, match.Value);
        }

        foreach (var ch in text)
        {
            if (IsCjk(ch))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddToken(vector, ch.ToString());
            }
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    private static void AddToken(float[] vector, string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var index = BitConverter.ToUInt32(hash, 0) % (uint)vector.Length;
        var sign = (hash[4] & 1) == 0 ? 1f : -1f;
        vector[index] += sign;
    }

    private static bool IsCjk(char ch) =>
        ch is >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF';

    private static void Normalize(float[] vector)
    {
        var norm = Math.Sqrt(vector.Sum(value => value * value));
        if (norm <= 0)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }
}

public sealed class LocalContextChatProvider : IChatProvider
{
    private readonly ProviderConfig _config;

    public LocalContextChatProvider(ProviderConfig config)
    {
        _config = config;
    }

    public string ProviderId => _config.ProviderId;

    public Task<AnswerRoute> PlanAnswerRouteAsync(
        string question,
        IReadOnlyList<ChatTurn> history,
        PromptProfile promptProfile,
        CancellationToken cancellationToken) =>
        Task.FromResult(new AnswerRoute("knowledge", "", question));

    public Task<string> CreateSearchQueryAsync(
        string question,
        IReadOnlyList<ChatTurn> history,
        PromptProfile promptProfile,
        CancellationToken cancellationToken) =>
        Task.FromResult(question);

    public Task<string?> CreateFollowUpSearchQueryAsync(
        string question,
        IReadOnlyList<SearchResult> citations,
        IReadOnlyList<ChatTurn> history,
        PromptProfile promptProfile,
        CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);

    public Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (request.Citations.Count == 0 || request.Citations.All(citation => citation.Score <= 0))
        {
            return Task.FromResult("知识库里没有检索到足够相关的内容。请先导入资料，或换一种问法。");
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.PromptProfile.CharacterPrompt))
        {
            builder.AppendLine(request.PromptProfile.CharacterPrompt);
            builder.AppendLine();
        }

        if (request.PromptProfile.ActiveWorldBookEntries.Count > 0)
        {
            builder.AppendLine("已触发世界书：");
            foreach (var entry in request.PromptProfile.ActiveWorldBookEntries)
            {
                builder.Append("- ");
                builder.Append(entry.Name);
                builder.Append(": ");
                builder.AppendLine(Trim(entry.Content, 220));
            }

            builder.AppendLine();
        }

        builder.AppendLine("我在知识库中找到以下相关内容：");
        var citationLimit = RagQueryClassifier.IsBroadInventoryQuestion(request.Question) ? 12 : 3;
        foreach (var citation in request.Citations.Take(citationLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append("- ");
            builder.Append(citation.Chunk.SourceLabel);
            builder.Append(": ");
            builder.AppendLine(Trim(citation.Chunk.Text, 260));
        }

        builder.AppendLine();
        builder.Append("问题：");
        builder.AppendLine(request.Question);
        builder.AppendLine("请配置 OpenAI-compatible Chat provider 后，可生成更自然的综合回答。");
        return Task.FromResult(builder.ToString());
    }

    private static string Trim(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "...";
}

public sealed class OpenAiCompatibleChatProvider : IChatProvider
{
    private readonly ProviderConfig _config;
    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleChatProvider(ProviderConfig config, ICredentialService credentialService, HttpClient httpClient)
    {
        _config = config;
        _credentialService = credentialService;
        _httpClient = httpClient;
    }

    public string ProviderId => _config.ProviderId;

    public async Task<AnswerRoute> PlanAnswerRouteAsync(
        string question,
        IReadOnlyList<ChatTurn> history,
        PromptProfile promptProfile,
        CancellationToken cancellationToken)
    {
        var messages = new object[]
        {
            new
            {
                role = "system",
                content = """
你是触屏问答智能体的路由与短答控制器。你只判断是否需要查询本地知识库，并在不需要时给出短回答。

输出必须是 JSON，不要 Markdown，不要解释：
{"mode":"direct|knowledge","directAnswer":"...","searchQuery":"..."}

规则：
1. 寒暄、唤醒、叫助手名字、确认在线、问“你是谁/你能做什么/怎么使用”、感谢、再见、简单操作提示，mode=direct。
2. direct 回答必须非常短，通常 1 句；唤醒/叫名字只需自然回应在场，例如“在，我在呢。”，不要展开介绍，除非用户追问。
3. 涉及学校资料、专业、招生、地址、电话、时间、流程、政策、业务办理、窗口、路线、知识库事实，mode=knowledge。
4. 不确定是否需要事实依据时，mode=knowledge。
5. mode=knowledge 时 directAnswer 为空，searchQuery 写适合向量检索的中文检索词。
6. mode=direct 时 searchQuery 为空，directAnswer 要遵守角色设定和系统提示词。
"""
            },
            new
            {
                role = "user",
                content = $"最近对话:\n{FormatHistory(history)}\n\n系统/角色/世界书:\n{BuildSystemContent(promptProfile)}\n\n用户输入:\n{question}\n\n请输出 JSON："
            }
        };

        var raw = await CompleteMessagesAsync(messages, 0, includeThinkingSwitch: false, cancellationToken);
        return ParseAnswerRoute(raw, question);
    }

    public async Task<string> CreateSearchQueryAsync(
        string question,
        IReadOnlyList<ChatTurn> history,
        PromptProfile promptProfile,
        CancellationToken cancellationToken)
    {
        var messages = new object[]
        {
            new
            {
                role = "system",
                content = "你是本地知识库检索查询规划器。你的任务是把用户问题改写成适合向量检索的中文检索词。只输出一行检索词，不要回答问题，不要解释。保留关键实体、业务名、专业名、同义词。"
            },
            new
            {
                role = "user",
                content = $"最近对话:\n{FormatHistory(history)}\n\n角色/规则:\n{BuildSystemContent(promptProfile)}\n\n用户问题:\n{question}\n\n请输出检索词："
            }
        };

        var query = CleanPlannerOutput(await CompleteMessagesAsync(messages, 0, includeThinkingSwitch: false, cancellationToken));
        return string.IsNullOrWhiteSpace(query) ? question : query;
    }

    public async Task<string?> CreateFollowUpSearchQueryAsync(
        string question,
        IReadOnlyList<SearchResult> citations,
        IReadOnlyList<ChatTurn> history,
        PromptProfile promptProfile,
        CancellationToken cancellationToken)
    {
        var context = FormatCitations(citations.Take(12));
        var messages = new object[]
        {
            new
            {
                role = "system",
                content = "你是知识库检索质量评估器。判断当前召回内容是否足够回答用户问题。如果足够，只输出 NONE。如果不足，只输出一行新的补充检索词，不要回答问题，不要解释。"
            },
            new
            {
                role = "user",
                content = $"用户问题:\n{question}\n\n当前召回内容:\n{context}\n\n如果需要补充检索，输出新的检索词；如果不需要，输出 NONE："
            }
        };

        var query = CleanPlannerOutput(await CompleteMessagesAsync(messages, 0, includeThinkingSwitch: false, cancellationToken));
        if (string.IsNullOrWhiteSpace(query) || query.Equals("NONE", StringComparison.OrdinalIgnoreCase) || query.Equals("无", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return query;
    }

    public async Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        return await CompleteMessagesAsync(BuildMessages(request), 0.2, includeThinkingSwitch: true, cancellationToken);
    }

    private async Task<string> CompleteMessagesAsync(
        object[] messages,
        double temperature,
        bool includeThinkingSwitch,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _config.Endpoint);
        AddAuthorization(httpRequest, _credentialService, _config.AuthRef);
        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = _config.Model,
            ["temperature"] = temperature,
            ["messages"] = messages
        };
        if (includeThinkingSwitch &&
            _config.Options.TryGetValue("enableThinking", out var enableThinkingValue) &&
            bool.TryParse(enableThinkingValue, out var enableThinking) &&
            ModelSupportsThinkingSwitch(_config.Model))
        {
            requestBody["enable_thinking"] = enableThinking;
        }

        httpRequest.Content = JsonContent.Create(requestBody);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderHttpException("chat/completions", _config.ProviderId, _config.Model, _config.Endpoint, response.StatusCode, body);
        }

        var json = JsonNode.Parse(body);
        return json?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Chat response did not contain choices[0].message.content.");
    }

    private static object[] BuildMessages(ChatRequest request)
    {
        var context = string.Join(Environment.NewLine, request.Citations.Select((citation, index) =>
            $"[{index + 1}] {citation.Chunk.SourceLabel} {citation.Chunk.Text}"));
        var answerInstruction = RagQueryClassifier.IsBroadInventoryQuestion(request.Question)
            ? "回答要求：用户在询问完整清单。请尽量从知识库上下文中提取全部不同条目，不要只列前 5 个；如果上下文仍不完整，要明确说明“当前召回内容可能不完整”，并给出已找到的条目。"
            : "回答要求：根据知识库上下文直接回答，缺少依据时说明缺少依据。";
        var styleInstruction = "表达要求：不要照搬知识库原文，先理解后用面向游客的自然中文重新组织；不要输出 Markdown 语法，不要使用 **、#、```、表格；不要在正文里写 [1]、[2]、[3][4] 这类引用标号；如需分点，用“1. 2. 3.”或短句分行即可。";
        var messages = new List<object>
        {
            new
            {
                role = "system",
                content = BuildSystemContent(request.PromptProfile)
            },
            new
            {
                role = "user",
                content = $"知识库上下文:\n{context}\n\n{answerInstruction}\n{styleInstruction}\n\n用户问题:\n{request.Question}"
            }
        };

        return messages.ToArray();
    }

    private static string FormatHistory(IReadOnlyList<ChatTurn> history) =>
        string.Join(Environment.NewLine, history.TakeLast(6).Select(turn => $"{turn.Role}: {TrimForPrompt(turn.Content, 180)}"));

    private static string FormatCitations(IEnumerable<SearchResult> citations) =>
        string.Join(Environment.NewLine, citations.Select((citation, index) =>
            $"[{index + 1}] score={citation.Score:0.000} {citation.Chunk.SourceLabel} {TrimForPrompt(citation.Chunk.Text, 360)}"));

    private static string CleanPlannerOutput(string value)
    {
        var cleaned = (value ?? "").Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
                .Replace("```text", "", StringComparison.OrdinalIgnoreCase)
                .Replace("```", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        foreach (var prefix in new[] { "检索词：", "检索词:", "SEARCH:", "search:", "查询：", "查询:" })
        {
            if (cleaned.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[prefix.Length..].Trim();
            }
        }

        return cleaned.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
    }

    private static AnswerRoute ParseAnswerRoute(string raw, string question)
    {
        var cleaned = StripCodeFence(raw);
        try
        {
            var json = JsonNode.Parse(cleaned);
            var mode = json?["mode"]?.GetValue<string>()?.Trim().ToLowerInvariant() ?? "knowledge";
            var directAnswer = json?["directAnswer"]?.GetValue<string>()?.Trim() ?? "";
            var searchQuery = json?["searchQuery"]?.GetValue<string>()?.Trim() ?? "";
            if (mode == "direct" && !string.IsNullOrWhiteSpace(directAnswer))
            {
                return new AnswerRoute("direct", directAnswer, "");
            }

            return new AnswerRoute("knowledge", "", string.IsNullOrWhiteSpace(searchQuery) ? question : searchQuery);
        }
        catch
        {
            var oneLine = CleanPlannerOutput(cleaned);
            if (oneLine.StartsWith("direct:", StringComparison.OrdinalIgnoreCase) ||
                oneLine.StartsWith("直接:", StringComparison.OrdinalIgnoreCase))
            {
                var answer = oneLine[(oneLine.IndexOf(':') + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    return new AnswerRoute("direct", answer, "");
                }
            }

            return new AnswerRoute("knowledge", "", question);
        }
    }

    private static string StripCodeFence(string value)
    {
        var cleaned = (value ?? "").Trim();
        if (!cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            return cleaned;
        }

        return cleaned.Replace("```json", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```text", "", StringComparison.OrdinalIgnoreCase)
            .Replace("```", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string TrimForPrompt(string text, int maxLength) =>
        string.IsNullOrWhiteSpace(text)
            ? ""
            : text.Length <= maxLength ? text : text[..maxLength] + "...";

    public static bool ModelSupportsThinkingSwitch(string model)
    {
        var normalized = (model ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("qwen3", StringComparison.Ordinal) ||
            normalized.Contains("qwen-3", StringComparison.Ordinal) ||
            normalized.Contains("qwen3.7", StringComparison.Ordinal) ||
            normalized.Contains("qwen3.6", StringComparison.Ordinal) ||
            normalized.Contains("qwen3.5", StringComparison.Ordinal) ||
            normalized.Contains("qwq", StringComparison.Ordinal) ||
            normalized.Contains("qvq", StringComparison.Ordinal);
    }

    private static string BuildSystemContent(PromptProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.IsNullOrWhiteSpace(profile.SystemPrompt)
            ? AppSettings.DefaultSystemPrompt
            : profile.SystemPrompt.Trim());
        if (!string.IsNullOrWhiteSpace(profile.CharacterPrompt))
        {
            builder.AppendLine();
            builder.AppendLine("角色设定：");
            builder.AppendLine(profile.CharacterPrompt.Trim());
        }

        if (profile.ActiveWorldBookEntries.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("已触发世界书：");
            foreach (var entry in profile.ActiveWorldBookEntries)
            {
                builder.Append("- ");
                builder.Append(entry.Name);
                builder.Append(": ");
                builder.AppendLine(entry.Content.Trim());
            }
        }

        return builder.ToString();
    }

    private static void AddAuthorization(HttpRequestMessage request, ICredentialService credentialService, string authRef)
    {
        var secret = credentialService.ReadSecret(CredentialName(authRef));
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException($"Missing API key in {credentialService.StorageDescription}: {CredentialName(authRef)}");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
    }

    public static string CredentialName(string authRef) => $"KnowledgeBaseQaAgent/{authRef}";
}

public sealed class OpenAiCompatibleEmbeddingProvider : IEmbeddingProvider
{
    private readonly ProviderConfig _config;
    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleEmbeddingProvider(ProviderConfig config, ICredentialService credentialService, HttpClient httpClient)
    {
        _config = config;
        _credentialService = credentialService;
        _httpClient = httpClient;
    }

    public string ProviderId => _config.ProviderId;
    public int Dimensions => 1536;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.Endpoint);
        var secret = _credentialService.ReadSecret(OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef));
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException($"Missing API key in {_credentialService.StorageDescription}: {OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef)}");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        request.Content = JsonContent.Create(new { model = _config.Model, input = text });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderHttpException("embeddings", _config.ProviderId, _config.Model, _config.Endpoint, response.StatusCode, body);
        }

        var json = JsonNode.Parse(body);
        var values = json?["data"]?[0]?["embedding"]?.AsArray()
            ?? throw new InvalidOperationException("Embedding response did not contain data[0].embedding.");
        return values.Select(value => value!.GetValue<float>()).ToArray();
    }
}

public sealed class WindowsDictationRecognizer : ISpeechRecognizer
{
    public string ProviderId => "windows-dictation";

    public async Task<string> RecognizeOnceAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var recognizer = new SpeechRecognitionEngine(CultureInfo.CurrentCulture);
        recognizer.LoadGrammar(new DictationGrammar());
        recognizer.SetInputToDefaultAudioDevice();
        recognizer.RecognizeCompleted += (_, args) =>
        {
            if (args.Error is not null)
            {
                completion.TrySetException(args.Error);
            }
            else if (args.Cancelled)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            else
            {
                completion.TrySetResult(args.Result?.Text ?? "");
            }
        };

        await using var registration = cancellationToken.Register(() =>
        {
            recognizer.RecognizeAsyncCancel();
            completion.TrySetCanceled(cancellationToken);
        });
        recognizer.RecognizeAsync(RecognizeMode.Single);
        var completed = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(12), cancellationToken));
        if (completed != completion.Task)
        {
            recognizer.RecognizeAsyncCancel();
            throw new TimeoutException("No speech was recognized within 12 seconds.");
        }

        return await completion.Task;
    }
}

public sealed class MicrophoneAudioTranscriptionRecognizer : ISpeechRecognizer
{
    private static readonly TimeSpan CaptureDuration = TimeSpan.FromSeconds(6);
    private readonly IAudioTranscriber _transcriber;

    public MicrophoneAudioTranscriptionRecognizer(IAudioTranscriber transcriber)
    {
        _transcriber = transcriber;
    }

    public string ProviderId => _transcriber.ProviderId;

    public async Task<string> RecognizeOnceAsync(CancellationToken cancellationToken)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kbqa-asr-mic-{Guid.NewGuid():N}.wav");
        try
        {
            ProviderDiagnostics.Info($"Microphone ASR capture started: provider={ProviderId}, seconds={CaptureDuration.TotalSeconds:0.#}, output={tempFile}");
            await WindowsWaveRecorder.RecordAsync(tempFile, CaptureDuration, cancellationToken);
            ProviderDiagnostics.Info($"Microphone ASR capture saved: file={tempFile}, bytes={new FileInfo(tempFile).Length}");
            return await _transcriber.TranscribeFileAsync(tempFile, cancellationToken);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary microphone capture cleanup is best-effort.
        }
    }
}

public static class WindowsWaveRecorder
{
    public static async Task RecordAsync(string outputPath, TimeSpan duration, CancellationToken cancellationToken)
    {
        var alias = $"kbqa_rec_{Guid.NewGuid():N}";
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        try
        {
            Send($"open new type waveaudio alias {alias}");
            Send($"set {alias} time format ms bitspersample 16 samplespersec 16000 channels 1 bytespersec 32000 alignment 2");
            Send($"record {alias}");
            await Task.Delay(duration, cancellationToken);
            Send($"stop {alias}");
            Send($"save {alias} \"{outputPath}\"");
        }
        catch (OperationCanceledException)
        {
            TrySend($"stop {alias}");
            throw;
        }
        finally
        {
            TrySend($"close {alias}");
        }

        if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            throw new InvalidOperationException("麦克风录音失败：没有生成音频文件。请检查 Windows 麦克风权限和默认输入设备。");
        }
    }

    private static void Send(string command)
    {
        var error = mciSendString(command, null, 0, IntPtr.Zero);
        if (error == 0)
        {
            return;
        }

        var message = new StringBuilder(256);
        _ = mciGetErrorString(error, message, message.Capacity);
        throw new InvalidOperationException($"麦克风录音失败：{message} (MCI {error}, command={command})");
    }

    private static void TrySend(string command)
    {
        try
        {
            _ = mciSendString(command, null, 0, IntPtr.Zero);
        }
        catch
        {
            // MCI cleanup is best-effort.
        }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr winHandle);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);
}

public sealed class OpenAiCompatibleAudioTranscriber : IAudioTranscriber
{
    private readonly ProviderConfig _config;
    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleAudioTranscriber(ProviderConfig config, ICredentialService credentialService, HttpClient httpClient)
    {
        _config = config;
        _credentialService = credentialService;
        _httpClient = httpClient;
    }

    public string ProviderId => _config.ProviderId;

    public async Task<string> TranscribeFileAsync(string audioPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            throw new FileNotFoundException("ASR 测试音频不存在。", audioPath);
        }

        var secret = _credentialService.ReadSecret(OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef));
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException($"Missing API key in {_credentialService.StorageDescription}: {OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef)}");
        }

        ProviderDiagnostics.Info($"ASR file transcription request: provider={_config.ProviderId}, model={_config.Model}, endpoint={_config.Endpoint}, file={audioPath}, bytes={new FileInfo(audioPath).Length}");
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_config.Model), "model");
        await using var fileStream = File.OpenRead(audioPath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetAudioContentType(audioPath));
        form.Add(fileContent, "file", Path.GetFileName(audioPath));
        request.Content = form;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        ProviderDiagnostics.Info($"ASR file transcription response: status={(int)response.StatusCode} {response.StatusCode}, bodyChars={body.Length}");
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderHttpException("asr-transcription", _config.ProviderId, _config.Model, _config.Endpoint, response.StatusCode, body);
        }

        var json = JsonNode.Parse(body);
        var text = json?["text"]?.GetValue<string>() ??
            json?["output"]?["text"]?.GetValue<string>() ??
            json?["data"]?["text"]?.GetValue<string>() ??
            "";
        return string.IsNullOrWhiteSpace(text)
            ? body
            : text.Trim();
    }

    private static string GetAudioContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "audio/mp4",
            ".wav" => "audio/wav",
            ".webm" => "audio/webm",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            _ => "application/octet-stream"
        };
}

public sealed class AliyunQwenRealtimeAudioTranscriber : IAudioTranscriber
{
    private const int TargetSampleRate = 16000;
    private const int ChunkSize = 3200;
    private const int ReceiveTimeoutMs = 20000;
    private readonly ProviderConfig _config;
    private readonly ICredentialService _credentialService;

    public AliyunQwenRealtimeAudioTranscriber(ProviderConfig config, ICredentialService credentialService)
    {
        _config = config;
        _credentialService = credentialService;
    }

    public string ProviderId => _config.ProviderId;

    public async Task<string> TranscribeFileAsync(string audioPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            throw new FileNotFoundException("ASR 测试音频不存在。", audioPath);
        }

        var apiKey = _credentialService.ReadSecret(OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException($"Missing API key in {_credentialService.StorageDescription}: {OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef)}");
        }

        var endpoint = Required(_config.Endpoint, "请先保存阿里百炼 ASR 配置。Realtime ASR 需要 wss://.../api-ws/v1/realtime?model={model} Endpoint。")
            .Replace("{model}", Uri.EscapeDataString(_config.Model), StringComparison.OrdinalIgnoreCase);
        if (!endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("阿里百炼 Qwen ASR Realtime 不能调用 /compatible-mode/v1/audio/transcriptions。请填写业务空间 ID 并保存，程序会生成 wss://.../api-ws/v1/realtime?model=...。");
        }

        var pcm = LoadPcm16Mono(audioPath, TargetSampleRate);
        ProviderDiagnostics.Info($"Aliyun Qwen ASR realtime request: model={_config.Model}, endpoint={endpoint}, file={audioPath}, sourceBytes={new FileInfo(audioPath).Length}, pcmBytes={pcm.Length}, sampleRate={TargetSampleRate}");

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        ws.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
        var workspaceId = _config.Options.GetValueOrDefault("workspaceId", "").Trim();
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            ws.Options.SetRequestHeader("X-DashScope-WorkSpace", workspaceId);
        }

        ws.Options.SetRequestHeader("user-agent", "KnowledgeBaseQaAgent/1.0");
        await ws.ConnectAsync(new Uri(endpoint), cancellationToken);
        var receiveTask = ReceiveTranscriptAsync(ws, cancellationToken);

        await SendRealtimeEventAsync(ws, new
        {
            type = "session.update",
            session = new Dictionary<string, object?>
            {
                ["modalities"] = new[] { "text" },
                ["input_audio_format"] = "pcm",
                ["sample_rate"] = TargetSampleRate,
                ["turn_detection"] = null
            }
        }, cancellationToken);

        for (var offset = 0; offset < pcm.Length; offset += ChunkSize)
        {
            var count = Math.Min(ChunkSize, pcm.Length - offset);
            await SendRealtimeEventAsync(ws, new
            {
                type = "input_audio_buffer.append",
                audio = Convert.ToBase64String(pcm, offset, count)
            }, cancellationToken);
            await Task.Delay(80, cancellationToken);
        }

        await SendRealtimeEventAsync(ws, new { type = "input_audio_buffer.commit" }, cancellationToken);
        await SendRealtimeEventAsync(ws, new { type = "session.finish" }, cancellationToken);

        var transcript = await receiveTask;
        if (ws.State == WebSocketState.Open)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "ASR finished", CancellationToken.None);
        }

        ProviderDiagnostics.Info($"Aliyun Qwen ASR realtime transcript: chars={transcript.Length}, text={TrimLogValue(transcript, 200)}");
        return string.IsNullOrWhiteSpace(transcript)
            ? throw new InvalidOperationException("阿里百炼 Qwen ASR Realtime 没有返回转写文本。请确认音频是清晰人声，或换一段更长的测试音频。")
            : transcript.Trim();
    }

    private static async Task<string> ReceiveTranscriptAsync(ClientWebSocket ws, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        var transcript = "";
        var eventCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await ReceiveWithTimeoutAsync(ws, buffer, ReceiveTimeoutMs, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    ProviderDiagnostics.Info($"Aliyun Qwen ASR realtime socket closed by server: status={result.CloseStatus}, description={result.CloseStatusDescription}");
                    return transcript;
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var body = Encoding.UTF8.GetString(message.ToArray());
            var json = JsonNode.Parse(body);
            var type = json?["type"]?.GetValue<string>() ?? "";
            eventCounts[type] = eventCounts.GetValueOrDefault(type) + 1;
            if (type == "conversation.item.input_audio_transcription.completed")
            {
                transcript = json?["transcript"]?.GetValue<string>() ?? transcript;
            }
            else if (type == "conversation.item.input_audio_transcription.text")
            {
                var text = json?["text"]?.GetValue<string>() ?? "";
                var stash = json?["stash"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrWhiteSpace(text + stash))
                {
                    transcript = text + stash;
                }
            }
            else if (type == "session.finished")
            {
                ProviderDiagnostics.Info($"Aliyun Qwen ASR realtime events: {FormatEventCounts(eventCounts)}");
                return transcript;
            }
            else if (type == "error")
            {
                throw new InvalidOperationException($"阿里百炼 Qwen ASR Realtime 错误：{json?["error"]}");
            }
        }

        return transcript;
    }

    private static async Task SendRealtimeEventAsync(ClientWebSocket ws, object payload, CancellationToken cancellationToken)
    {
        var node = JsonSerializer.SerializeToNode(payload)!.AsObject();
        node["event_id"] = $"event_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}";
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<WebSocketReceiveResult> ReceiveWithTimeoutAsync(
        ClientWebSocket ws,
        byte[] buffer,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);
        try
        {
            return await ws.ReceiveAsync(buffer, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Realtime ASR receive timed out after {timeoutMs} ms.");
        }
    }

    private static byte[] LoadPcm16Mono(string path, int targetSampleRate)
    {
        if (Path.GetExtension(path).Equals(".pcm", StringComparison.OrdinalIgnoreCase))
        {
            return File.ReadAllBytes(path);
        }

        if (!Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("阿里百炼 Qwen ASR Realtime 文件测试当前支持 WAV/PCM。MP3/M4A 请改用非 realtime 文件转写模型，或先转成 16kHz 单声道 WAV。");
        }

        var wav = ReadWavPcm16(path);
        return wav.SampleRate == targetSampleRate
            ? wav.Pcm
            : ResamplePcm16Mono(wav.Pcm, wav.SampleRate, targetSampleRate);
    }

    private static (byte[] Pcm, int SampleRate) ReadWavPcm16(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
        {
            throw new InvalidOperationException("WAV 文件格式不正确：缺少 RIFF 头。");
        }

        reader.ReadInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
        {
            throw new InvalidOperationException("WAV 文件格式不正确：缺少 WAVE 标记。");
        }

        short audioFormat = 0;
        short channels = 0;
        var sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? data = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = reader.ReadInt32();
            var chunkStart = stream.Position;
            if (chunkId == "fmt ")
            {
                audioFormat = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                bitsPerSample = reader.ReadInt16();
            }
            else if (chunkId == "data")
            {
                data = reader.ReadBytes(chunkSize);
            }

            stream.Position = chunkStart + chunkSize + (chunkSize % 2);
        }

        if (audioFormat != 1 || bitsPerSample != 16 || channels <= 0 || sampleRate <= 0 || data is null)
        {
            throw new InvalidOperationException($"WAV 必须是 PCM 16-bit 音频。当前 format={audioFormat}, channels={channels}, sampleRate={sampleRate}, bits={bitsPerSample}。");
        }

        return (channels == 1 ? data : MixToMonoPcm16(data, channels), sampleRate);
    }

    private static byte[] MixToMonoPcm16(byte[] pcm, short channels)
    {
        var frameCount = pcm.Length / 2 / channels;
        var mono = new byte[frameCount * 2];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0;
            for (var channel = 0; channel < channels; channel++)
            {
                var index = (frame * channels + channel) * 2;
                sum += BitConverter.ToInt16(pcm, index);
            }

            var sample = (short)(sum / channels);
            BitConverter.GetBytes(sample).CopyTo(mono, frame * 2);
        }

        return mono;
    }

    private static byte[] ResamplePcm16Mono(byte[] pcm, int sourceRate, int targetRate)
    {
        var sourceSamples = pcm.Length / 2;
        var targetSamples = Math.Max(1, (int)Math.Round(sourceSamples * (double)targetRate / sourceRate));
        var output = new byte[targetSamples * 2];
        for (var i = 0; i < targetSamples; i++)
        {
            var sourcePosition = i * (double)sourceRate / targetRate;
            var left = Math.Min(sourceSamples - 1, (int)Math.Floor(sourcePosition));
            var right = Math.Min(sourceSamples - 1, left + 1);
            var fraction = sourcePosition - left;
            var leftSample = BitConverter.ToInt16(pcm, left * 2);
            var rightSample = BitConverter.ToInt16(pcm, right * 2);
            var sample = (short)Math.Clamp((int)Math.Round(leftSample + (rightSample - leftSample) * fraction), short.MinValue, short.MaxValue);
            BitConverter.GetBytes(sample).CopyTo(output, i * 2);
        }

        return output;
    }

    private static string FormatEventCounts(Dictionary<string, int> eventCounts) =>
        string.Join(", ", eventCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}"));

    private static string TrimLogValue(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static string Required(string value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value.Trim();
}

public sealed class WindowsTextToSpeech : ISpeechSynthesizer
{
    public string ProviderId => "windows-tts";

    public Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            using var synth = new SpeechSynthesizer();
            using var registration = cancellationToken.Register(() => synth.SpeakAsyncCancelAll());
            synth.SetOutputToDefaultAudioDevice();
            synth.Speak(text);
        }, cancellationToken);
    }
}

public sealed class OpenAiCompatibleSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly ProviderConfig _config;
    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public OpenAiCompatibleSpeechSynthesizer(ProviderConfig config, ICredentialService credentialService, HttpClient httpClient)
    {
        _config = config;
        _credentialService = credentialService;
        _httpClient = httpClient;
    }

    public string ProviderId => _config.ProviderId;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        if (_config.Model.Contains("qwen3-tts", StringComparison.OrdinalIgnoreCase) ||
            (_config.Endpoint.Contains("dashscope.aliyuncs.com/compatible-mode", StringComparison.OrdinalIgnoreCase) &&
                _config.Endpoint.Contains("/audio/speech", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("当前 TTS 仍在使用 OpenAI-compatible TTS，但模型或 Endpoint 是阿里百炼 Qwen TTS。请在“检索与语音”里选择“阿里百炼 Qwen TTS”并保存；Qwen TTS 不能走 /compatible-mode/v1/audio/speech。");
        }

        var secret = _credentialService.ReadSecret(OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef));
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException($"Missing API key in {_credentialService.StorageDescription}: {OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef)}");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, _config.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        request.Content = JsonContent.Create(new
        {
            model = _config.Model,
            voice = _config.Options.GetValueOrDefault("voice", "alloy"),
            input = text,
            response_format = "wav"
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await ProviderHttpDiagnostics.EnsureSuccessAsync(response, "openai-compatible-tts", _config.ProviderId, _config.Model, _config.Endpoint, cancellationToken);
        var tempFile = Path.Combine(Path.GetTempPath(), $"kbqa-tts-{Guid.NewGuid():N}.wav");
        await using (var stream = File.Create(tempFile))
        {
            await response.Content.CopyToAsync(stream, cancellationToken);
        }

        using var player = new SoundPlayer(tempFile);
        player.PlaySync();
        File.Delete(tempFile);
    }
}

public sealed class AliyunQwenSpeechSynthesizer : ISpeechSynthesizer
{
    private static readonly TimeSpan HttpTtsTimeout = TimeSpan.FromSeconds(35);
    private readonly ProviderConfig _config;
    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public AliyunQwenSpeechSynthesizer(ProviderConfig config, ICredentialService credentialService, HttpClient httpClient)
    {
        _config = config;
        _credentialService = credentialService;
        _httpClient = httpClient;
    }

    public string ProviderId => _config.ProviderId;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        if (_config.Model.Contains("realtime", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await SpeakRealtimeAsync(text, cancellationToken);
            }
            catch (Exception ex) when (ShouldFallbackToHttpTts(ex))
            {
                ProviderDiagnostics.Error($"Aliyun Qwen TTS realtime failed, falling back to HTTP. Type={ex.GetType().Name}, Message={ex.Message}");
                await SpeakHttpSegmentedAsync(
                    text,
                    cancellationToken,
                    NormalizeRealtimeModelForHttp(_config.Model),
                    BuildAliyunQwenHttpEndpoint());
            }

            return;
        }

        await SpeakHttpSegmentedAsync(text, cancellationToken);
    }

    private async Task SpeakHttpSegmentedAsync(
        string text,
        CancellationToken cancellationToken,
        string? modelOverride = null,
        string? endpointOverride = null)
    {
        var segments = SplitAliyunTtsText(text);
        if (segments.Count == 0)
        {
            return;
        }

        ProviderDiagnostics.Info($"Aliyun Qwen TTS HTTP segmented request: segments={segments.Count}, chars={segments.Sum(segment => segment.Length)}");
        foreach (var segment in segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SpeakHttpAsync(segment, cancellationToken, modelOverride, endpointOverride);
        }
    }

    private async Task SpeakHttpAsync(
        string text,
        CancellationToken cancellationToken,
        string? modelOverride = null,
        string? endpointOverride = null)
    {
        var apiKey = ReadApiKey();
        var model = Required(modelOverride, _config.Model);
        var endpoint = Required(endpointOverride, Required(_config.Endpoint, "请配置阿里百炼 Qwen TTS HTTP Endpoint。"));
        ProviderDiagnostics.Info($"Aliyun Qwen TTS HTTP request: model={model}, endpoint={endpoint}, textChars={text.Length}, voice={_config.Options.GetValueOrDefault("voice", "")}");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        AddWorkspaceHeader(request);
        var input = new Dictionary<string, object?>
        {
            ["text"] = text,
            ["voice"] = Required(_config.Options.GetValueOrDefault("voice"), "Cherry"),
            ["language_type"] = Required(_config.Options.GetValueOrDefault("language_type"), "Chinese")
        };
        var instructions = _config.Options.GetValueOrDefault("instructions", "").Trim();
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            input["instructions"] = instructions;
            input["optimize_instructions"] = bool.TryParse(_config.Options.GetValueOrDefault("optimize_instructions"), out var optimize) && optimize;
        }

        request.Content = JsonContent.Create(new
        {
            model,
            input
        });

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(HttpTtsTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"阿里百炼 Qwen TTS HTTP 请求超过 {HttpTtsTimeout.TotalSeconds:0} 秒未返回。建议使用 Realtime TTS，或切回本地 VITS。");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            ProviderDiagnostics.Info($"Aliyun Qwen TTS HTTP response: status={(int)response.StatusCode} {response.StatusCode}, bodyChars={responseBody.Length}");
            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderHttpException("aliyun-qwen-tts-http", _config.ProviderId, model, endpoint, response.StatusCode, responseBody);
            }

            var audioUrl = ExtractAudioUrl(responseBody);
            if (string.IsNullOrWhiteSpace(audioUrl))
            {
                throw new InvalidOperationException("阿里百炼 Qwen TTS 响应里没有找到音频 URL。");
            }

            var audioBytes = await _httpClient.GetByteArrayAsync(audioUrl, cancellationToken);
            ProviderDiagnostics.Info($"Aliyun Qwen TTS HTTP audio downloaded: bytes={audioBytes.Length}, url={TrimLogValue(audioUrl, 160)}");
            await PlayAudioBytesAsync(audioBytes, GuessAudioExtension(audioUrl, responseBody), cancellationToken);
        }
    }

    private static IReadOnlyList<string> SplitAliyunTtsText(string text)
    {
        const int maxChars = 520;
        const int minChars = 20;
        var normalized = Regex.Replace((text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), @"[ \t]+", " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var segments = new List<string>();
        var builder = new StringBuilder();
        var lastSoftBreak = -1;
        foreach (var ch in normalized)
        {
            builder.Append(ch);
            if (IsAliyunTtsSoftBreak(ch))
            {
                lastSoftBreak = builder.Length;
            }

            if (IsAliyunTtsHardBreak(ch) && builder.Length >= minChars)
            {
                FlushAliyunTtsSegment(builder, segments);
                lastSoftBreak = -1;
                continue;
            }

            if (builder.Length >= maxChars)
            {
                if (lastSoftBreak >= minChars && lastSoftBreak < builder.Length)
                {
                    var segment = builder.ToString(0, lastSoftBreak).Trim();
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        segments.Add(segment);
                    }

                    builder.Remove(0, lastSoftBreak);
                    lastSoftBreak = -1;
                    for (var i = 0; i < builder.Length; i++)
                    {
                        if (IsAliyunTtsSoftBreak(builder[i]))
                        {
                            lastSoftBreak = i + 1;
                        }
                    }
                }
                else
                {
                    FlushAliyunTtsSegment(builder, segments);
                    lastSoftBreak = -1;
                }
            }
        }

        FlushAliyunTtsSegment(builder, segments);
        return MergeShortAliyunTtsSegments(segments, minChars, maxChars);
    }

    private static IReadOnlyList<string> MergeShortAliyunTtsSegments(IReadOnlyList<string> segments, int minChars, int maxChars)
    {
        var merged = new List<string>();
        foreach (var segment in segments.Where(segment => !string.IsNullOrWhiteSpace(segment)))
        {
            var value = segment.Trim();
            if (merged.Count > 0 &&
                value.Length < minChars &&
                merged[^1].Length + value.Length + 1 <= maxChars)
            {
                merged[^1] = $"{merged[^1]} {value}";
            }
            else
            {
                merged.Add(value);
            }
        }

        return merged;
    }

    private static void FlushAliyunTtsSegment(StringBuilder builder, List<string> segments)
    {
        var value = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            segments.Add(value);
        }

        builder.Clear();
    }

    private static bool IsAliyunTtsHardBreak(char ch) =>
        ch is '\n' or '。' or '！' or '？' or '；' or ';' or '!' or '?' or '.';

    private static bool IsAliyunTtsSoftBreak(char ch) =>
        IsAliyunTtsHardBreak(ch) || ch is '，' or '、' or ',' or ':' or '：';

    private async Task SpeakRealtimeAsync(string text, CancellationToken cancellationToken)
    {
        var apiKey = ReadApiKey();
        var firstAudioTimeoutMs = ReadOptionInt("realtimeFirstAudioTimeoutMs", 8000);
        var idleTimeoutMs = ReadOptionInt("realtimeIdleTimeoutMs", 15000);
        var endpoint = Required(_config.Options.GetValueOrDefault("realtimeEndpoint"), "请配置阿里百炼 Qwen TTS Realtime WebSocket Endpoint。")
            .Replace("{model}", Uri.EscapeDataString(_config.Model), StringComparison.OrdinalIgnoreCase);
        if (!endpoint.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Realtime TTS 需要 wss:// WebSocket Endpoint，例如 wss://{WorkspaceId}.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime?model={model}");
        }

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        var workspaceId = _config.Options.GetValueOrDefault("workspaceId", "").Trim();
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            ws.Options.SetRequestHeader("X-DashScope-WorkSpace", workspaceId);
        }

        ws.Options.SetRequestHeader("user-agent", "KnowledgeBaseQaAgent/1.0");
        ProviderDiagnostics.Info($"Aliyun Qwen TTS realtime connect: endpoint={endpoint}, model={_config.Model}, textChars={text.Length}, voice={_config.Options.GetValueOrDefault("voice", "")}");
        await ws.ConnectAsync(new Uri(endpoint), cancellationToken);
        await SendRealtimeEventAsync(ws, new
        {
            type = "session.update",
            session = new Dictionary<string, object?>
            {
                ["mode"] = "commit",
                ["voice"] = Required(_config.Options.GetValueOrDefault("voice"), "Cherry"),
                ["language_type"] = Required(_config.Options.GetValueOrDefault("language_type"), "Chinese"),
                ["response_format"] = "pcm",
                ["sample_rate"] = 24000,
                ["instructions"] = EmptyToNull(_config.Options.GetValueOrDefault("instructions")),
                ["optimize_instructions"] = bool.TryParse(_config.Options.GetValueOrDefault("optimize_instructions"), out var optimize) && optimize
            }
        }, cancellationToken);
        await SendRealtimeEventAsync(ws, new { type = "input_text_buffer.append", text }, cancellationToken);
        await SendRealtimeEventAsync(ws, new { type = "input_text_buffer.commit" }, cancellationToken);

        await using var pcm = new MemoryStream();
        var eventCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[64 * 1024];
        var receivedAudio = false;
        while (!cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                var receiveTimeoutMs = receivedAudio ? idleTimeoutMs : firstAudioTimeoutMs;
                result = await ReceiveWithTimeoutAsync(ws, buffer, receiveTimeoutMs, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("阿里百炼 Qwen TTS Realtime 连接提前关闭。");
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            var json = JsonNode.Parse(Encoding.UTF8.GetString(message.ToArray()));
            var type = json?["type"]?.GetValue<string>() ?? "";
            eventCounts[type] = eventCounts.GetValueOrDefault(type) + 1;
            if (type == "response.audio.delta")
            {
                var delta = json?["delta"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrWhiteSpace(delta))
                {
                    var bytes = Convert.FromBase64String(delta);
                    await pcm.WriteAsync(bytes, cancellationToken);
                    receivedAudio = true;
                }
            }
            else if (type == "response.audio.done")
            {
                if (pcm.Length > 0)
                {
                    break;
                }
            }
            else if (type == "response.done")
            {
                break;
            }
            else if (type == "error")
            {
                throw new InvalidOperationException($"阿里百炼 Qwen TTS Realtime 错误：{json?["error"]}");
            }
        }

        ProviderDiagnostics.Info($"Aliyun Qwen TTS realtime events: {FormatEventCounts(eventCounts)}, pcmBytes={pcm.Length}");
        if (pcm.Length == 0)
        {
            throw new InvalidOperationException("Realtime TTS completed without audio bytes.");
        }

        await SendRealtimeEventAsync(ws, new { type = "session.finish" }, CancellationToken.None);
        var wavBytes = CreateWav(pcm.ToArray(), sampleRate: 24000, channels: 1, bitsPerSample: 16);
        ProviderDiagnostics.Info($"Aliyun Qwen TTS realtime WAV prepared: bytes={wavBytes.Length}");
        await PlayAudioBytesAsync(wavBytes, ".wav", cancellationToken);
    }

    private static bool ShouldFallbackToHttpTts(Exception ex) =>
        ex is WebSocketException ||
        ex is TimeoutException ||
        ex is UriFormatException ||
        ex is InvalidOperationException invalidOperation &&
        invalidOperation.Message.Contains("Realtime", StringComparison.OrdinalIgnoreCase);

    private string BuildAliyunQwenHttpEndpoint()
    {
        return "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
    }

    private static string NormalizeRealtimeModelForHttp(string model)
    {
        if (model.Contains("qwen3-tts-instruct-flash", StringComparison.OrdinalIgnoreCase))
        {
            return "qwen3-tts-instruct-flash";
        }

        if (model.Contains("qwen3-tts-flash", StringComparison.OrdinalIgnoreCase))
        {
            return "qwen3-tts-flash";
        }

        return model.Replace("-realtime", "", StringComparison.OrdinalIgnoreCase);
    }

    private void AddWorkspaceHeader(HttpRequestMessage request)
    {
        var workspaceId = _config.Options.GetValueOrDefault("workspaceId", "").Trim();
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            request.Headers.TryAddWithoutValidation("X-DashScope-WorkSpace", workspaceId);
        }
    }

    private int ReadOptionInt(string key, int fallback)
    {
        return int.TryParse(_config.Options.GetValueOrDefault(key), out var value) && value > 0
            ? value
            : fallback;
    }

    private string ReadApiKey()
    {
        var secret = _credentialService.ReadSecret(OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef));
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException($"Missing API key in {_credentialService.StorageDescription}: {OpenAiCompatibleChatProvider.CredentialName(_config.AuthRef)}");
        }

        return secret;
    }

    private static async Task SendRealtimeEventAsync(ClientWebSocket ws, object payload, CancellationToken cancellationToken)
    {
        var node = JsonSerializer.SerializeToNode(payload)!.AsObject();
        node["event_id"] = $"event_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}";
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<WebSocketReceiveResult> ReceiveWithTimeoutAsync(
        ClientWebSocket ws,
        byte[] buffer,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);
        try
        {
            return await ws.ReceiveAsync(buffer, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Realtime TTS receive timed out after {timeoutMs} ms.");
        }
    }

    private static string ExtractAudioUrl(string responseBody)
    {
        var json = JsonNode.Parse(responseBody);
        return json?["output"]?["audio"]?["url"]?.GetValue<string>()
            ?? json?["output"]?["url"]?.GetValue<string>()
            ?? json?["audio"]?["url"]?.GetValue<string>()
            ?? "";
    }

    private static async Task PlayAudioBytesAsync(byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("TTS audio is empty.");
        }

        var normalizedExtension = string.IsNullOrWhiteSpace(extension) ? ".wav" : extension;
        if (!normalizedExtension.StartsWith('.'))
        {
            normalizedExtension = "." + normalizedExtension;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"kbqa-aliyun-tts-{Guid.NewGuid():N}{normalizedExtension}");
        try
        {
            await File.WriteAllBytesAsync(tempFile, bytes, cancellationToken);
            var fileInfo = new FileInfo(tempFile);
            ProviderDiagnostics.Info($"TTS audio file ready: path={tempFile}, bytes={fileInfo.Length}");
            await PlayWithMediaPlayerAsync(tempFile, cancellationToken);
        }
        finally
        {
            TryDelete(tempFile);
        }
    }

    private static async Task PlayWithMediaPlayerAsync(string path, CancellationToken cancellationToken)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            await Task.Run(() =>
            {
                using var player = new SoundPlayer(path);
                player.PlaySync();
            }, cancellationToken);
            return;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await dispatcher.InvokeAsync(() =>
        {
            var player = new System.Windows.Media.MediaPlayer();
            player.MediaEnded += (_, _) =>
            {
                player.Close();
                completion.TrySetResult();
            };
            player.MediaFailed += (_, args) =>
            {
                player.Close();
                completion.TrySetException(args.ErrorException ?? new InvalidOperationException("TTS audio playback failed."));
            };
            player.Open(new Uri(path, UriKind.Absolute));
            player.Play();
        });

        await completion.Task.WaitAsync(cancellationToken);
    }

    private static string GuessAudioExtension(string audioUrl, string responseBody)
    {
        var path = Uri.TryCreate(audioUrl, UriKind.Absolute, out var uri) ? uri.AbsolutePath : audioUrl;
        var extension = Path.GetExtension(path);
        if (!string.IsNullOrWhiteSpace(extension) &&
            extension.Length <= 6)
        {
            return extension;
        }

        if (responseBody.Contains("\"format\":\"mp3\"", StringComparison.OrdinalIgnoreCase))
        {
            return ".mp3";
        }

        return ".wav";
    }

    private static string FormatEventCounts(Dictionary<string, int> eventCounts) =>
        string.Join(", ", eventCounts.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Key}={pair.Value}"));

    private static string TrimLogValue(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= maxLength ? value : value[..maxLength] + "...";

    private static byte[] CreateWav(byte[] pcm, int sampleRate, short channels, short bitsPerSample)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var blockAlign = (short)(channels * bitsPerSample / 8);
        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
        return stream.ToArray();
    }

    private static string Required(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp audio cleanup failure is non-fatal.
        }
    }
}

public sealed class ExternalLocalSpeechSynthesizer : ISpeechSynthesizer
{
    private const int MinTtsSegmentChars = 10;
    private const int MaxTtsSegmentChars = 90;
    private const int DefaultSinglePassMaxChars = 140;
    private const int ZhLlSinglePassMaxChars = 70;
    private const int KokoroSinglePassMaxChars = 220;
    private readonly ProviderConfig _config;

    public ExternalLocalSpeechSynthesizer(ProviderConfig config)
    {
        _config = config;
    }

    public string ProviderId => _config.ProviderId;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        var executable = Expand(_config.Endpoint);
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            throw new FileNotFoundException("Local TTS executable not found. Configure Endpoint with the executable path.", executable);
        }

        var model = Expand(_config.Model);
        if (string.IsNullOrWhiteSpace(model) || !File.Exists(model))
        {
            throw new FileNotFoundException("Local TTS model not found. Configure Model with the model file path.", model);
        }

        EnsureLocalTtsDependencies(executable, model);
        var processPaths = ResolveLocalTtsProcessPaths(executable, model);
        var singlePassMaxChars = GetSinglePassMaxChars(model);
        var speechText = PrepareLocalSpeechText(text);
        var segments = SplitForStreamingTts(speechText, singlePassMaxChars).ToArray();
        if (segments.Length == 0)
        {
            return;
        }

        if (segments.Length == 1)
        {
            var outputFile = await SynthesizeSegmentAsync(segments[0], executable, model, processPaths, cancellationToken);
            try
            {
                await PlayLocalWavAsync(outputFile, cancellationToken);
            }
            finally
            {
                TryDelete(outputFile);
            }

            return;
        }

        await SpeakSegmentedAsync(segments, executable, model, processPaths, cancellationToken);
    }

    private async Task SpeakSegmentedAsync(
        IReadOnlyList<string> segments,
        string executable,
        string model,
        LocalTtsProcessPaths processPaths,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<LocalTtsSegmentAudio>(new BoundedChannelOptions(2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });

        var producer = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < segments.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var segment = segments[i];
                    var output = await SynthesizeSegmentAsync(segment, executable, model, processPaths, cancellationToken);
                    ProviderDiagnostics.Info($"Local command TTS segment ready: index={i + 1}/{segments.Count}, chars={segment.Length}, file={output}");
                    await channel.Writer.WriteAsync(new LocalTtsSegmentAudio(i, segment, output), cancellationToken);
                }

                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
        }, cancellationToken);

        ProviderDiagnostics.Info($"Local command TTS segmented playback: segments={segments.Count}, chars={segments.Sum(segment => segment.Length)}");
        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                ProviderDiagnostics.Info($"Local command TTS play segment: index={item.Index + 1}/{segments.Count}, chars={item.Text.Length}, file={item.OutputFile}");
                await PlayLocalWavAsync(item.OutputFile, cancellationToken);
            }
            finally
            {
                TryDelete(item.OutputFile);
            }
        }

        await producer;
    }

    private async Task<string> SynthesizeSegmentAsync(
        string text,
        string executable,
        string model,
        LocalTtsProcessPaths processPaths,
        CancellationToken cancellationToken)
    {
        var outputFile = Path.Combine(Path.GetTempPath(), $"kbqa-local-tts-{Guid.NewGuid():N}.wav");
        var textFile = Path.Combine(Path.GetTempPath(), $"kbqa-local-tts-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(textFile, text, Encoding.UTF8, cancellationToken);
        var processOutputFile = ToProcessPath(outputFile, mustExist: false);
        var processTextFile = ToProcessPath(textFile, mustExist: true);

        try
        {
            var template = _config.Options.GetValueOrDefault("arguments", "--model \"{model}\" --output \"{output}\" --text \"{text}\"");
            var modelDirectory = Path.GetDirectoryName(processPaths.ModelPath) ?? "";
            var voice = NormalizeLocalTtsVoice(model, _config.Options.GetValueOrDefault("voice", "0").Trim());
            var arguments = template
                .Replace("{model}", processPaths.ModelPath)
                .Replace("{modelDir}", modelDirectory)
                .Replace("{modelName}", Path.GetFileNameWithoutExtension(processPaths.ModelPath))
                .Replace("{voice}", string.IsNullOrWhiteSpace(voice) ? "0" : voice)
                .Replace("{output}", processOutputFile)
                .Replace("{textFile}", processTextFile)
                .Replace("{text}", EscapeArgument(text));
            var redirectStdin = _config.Options.GetValueOrDefault("inputMode", "argument")
                .Equals("stdin", StringComparison.OrdinalIgnoreCase);
            ProviderDiagnostics.Info($"Local command TTS request: exe={executable}, processExe={processPaths.ExecutablePath}, model={model}, processModel={processPaths.ModelPath}, voice={voice}, inputMode={_config.Options.GetValueOrDefault("inputMode", "argument")}, textChars={text.Length}, args={arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = processPaths.ExecutablePath,
                Arguments = arguments,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                RedirectStandardInput = redirectStdin,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };
            if (redirectStdin)
            {
                startInfo.StandardInputEncoding = Encoding.UTF8;
            }

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start local TTS executable.");
            if (startInfo.RedirectStandardInput)
            {
                await process.StandardInput.WriteLineAsync(text);
                process.StandardInput.Close();
            }

            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;
            var stdout = await stdoutTask;
            ProviderDiagnostics.Info($"Local command TTS exited: code={process.ExitCode}, stdoutChars={stdout.Length}, stderrChars={stderr.Length}");
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Local TTS command exited with code {process.ExitCode} ({FormatExitCode(process.ExitCode)}). " +
                    $"stdout: {Limit(stdout)} stderr: {Limit(stderr)}");
            }

            if (!File.Exists(outputFile))
            {
                throw new InvalidOperationException("Local TTS command completed but did not create the WAV output file.");
            }

            return outputFile;
        }
        catch
        {
            TryDelete(outputFile);
            throw;
        }
        finally
        {
            TryDelete(textFile);
        }
    }

    private static Task PlayLocalWavAsync(string outputFile, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            using var player = new SoundPlayer(outputFile);
            player.PlaySync();
        }, cancellationToken);

    private int GetSinglePassMaxChars(string model)
    {
        if (int.TryParse(_config.Options.GetValueOrDefault("singlePassMaxChars", ""), out var configured) &&
            configured > 0)
        {
            return configured;
        }

        return model.Contains("kokoro", StringComparison.OrdinalIgnoreCase)
            ? KokoroSinglePassMaxChars
            : model.Contains("sherpa-onnx-vits-zh-ll", StringComparison.OrdinalIgnoreCase)
                ? ZhLlSinglePassMaxChars
                : DefaultSinglePassMaxChars;
    }

    private static IReadOnlyList<string> SplitForStreamingTts(string text, int singlePassMaxChars)
    {
        var normalized = NormalizeSpeechText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        if (normalized.Length <= singlePassMaxChars && !normalized.Any(IsHardBreak))
        {
            return [normalized];
        }

        var segments = new List<string>();
        var builder = new StringBuilder();
        var lastSoftBreak = -1;
        foreach (var ch in normalized)
        {
            builder.Append(ch);
            if (IsSoftBreak(ch))
            {
                lastSoftBreak = builder.Length;
            }

            if (IsHardBreak(ch) && builder.Length >= MinTtsSegmentChars)
            {
                Flush(builder, segments);
                lastSoftBreak = -1;
                continue;
            }

            if (builder.Length >= MaxTtsSegmentChars)
            {
                if (lastSoftBreak >= MinTtsSegmentChars && lastSoftBreak < builder.Length)
                {
                    var segment = builder.ToString(0, lastSoftBreak).Trim();
                    if (!string.IsNullOrWhiteSpace(segment))
                    {
                        segments.Add(segment);
                    }

                    builder.Remove(0, lastSoftBreak);
                    lastSoftBreak = -1;
                    for (var i = 0; i < builder.Length; i++)
                    {
                        if (IsSoftBreak(builder[i]))
                        {
                            lastSoftBreak = i + 1;
                        }
                    }
                }
                else
                {
                    Flush(builder, segments);
                    lastSoftBreak = -1;
                }
            }
        }

        Flush(builder, segments);
        return MergeShortSegments(segments);
    }

    private static string PrepareLocalSpeechText(string text)
    {
        var normalized = NormalizeSpeechText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "";
        }

        // This text is only sent to TTS. The chat bubble keeps the original answer.
        normalized = Regex.Replace(normalized, @"\[(?:\d+)(?:\]\[\d+)*\]", "");
        normalized = Regex.Replace(normalized, @"\[\s*\d+(?:\s*[,，]\s*\d+)*\s*\]", "");
        normalized = Regex.Replace(normalized, @"(?m)^\s*#{1,6}\s*", "");
        normalized = normalized
            .Replace("**", "", StringComparison.Ordinal)
            .Replace("__", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal);

        normalized = Regex.Replace(
            normalized,
            @"(^|[\n。！？!?；;])\s*(\d{1,2})\s*[\.．、)]\s*",
            match => $"{match.Groups[1].Value}第{ToChineseNumber(match.Groups[2].Value)}，",
            RegexOptions.Multiline);
        normalized = Regex.Replace(normalized, @"(?m)^\s*[-*•]\s+", "项目，");
        normalized = Regex.Replace(normalized, @"([。！？!?；;：:])\s*", "$1\n");
        normalized = Regex.Replace(normalized, @"\n{2,}", "\n");
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        return normalized.Trim();
    }

    private static string NormalizeSpeechText(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        normalized = Regex.Replace(normalized, @"[ \t]+", " ");
        normalized = Regex.Replace(normalized, @"\n{2,}", "\n");
        return normalized.Trim();
    }

    private static string ToChineseNumber(string value)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ||
            number <= 0)
        {
            return value;
        }

        string[] digits = ["零", "一", "二", "三", "四", "五", "六", "七", "八", "九"];
        return number switch
        {
            < 10 => digits[number],
            10 => "十",
            < 20 => $"十{digits[number % 10]}",
            < 100 when number % 10 == 0 => $"{digits[number / 10]}十",
            < 100 => $"{digits[number / 10]}十{digits[number % 10]}",
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static IReadOnlyList<string> MergeShortSegments(IReadOnlyList<string> segments)
    {
        var merged = new List<string>();
        foreach (var segment in segments.Where(segment => !string.IsNullOrWhiteSpace(segment)))
        {
            var value = segment.Trim();
            if (merged.Count > 0 &&
                value.Length < MinTtsSegmentChars &&
                merged[^1].Length + value.Length <= MaxTtsSegmentChars &&
                !EndsWithStrongPause(merged[^1]))
            {
                merged[^1] = $"{merged[^1]} {value}".Trim();
            }
            else
            {
                merged.Add(value);
            }
        }

        return merged;
    }

    private static void Flush(StringBuilder builder, List<string> segments)
    {
        var value = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            segments.Add(value);
        }

        builder.Clear();
    }

    private static bool IsHardBreak(char ch) =>
        ch is '\n' or '。' or '！' or '？' or '；' or ';' or '!' or '?' or '.';

    private static bool IsSoftBreak(char ch) =>
        IsHardBreak(ch) || ch is '，' or '、' or ',' or ':' or '：';

    private static bool EndsWithStrongPause(string value) =>
        value.TrimEnd().LastOrDefault() is '。' or '！' or '？' or '；' or ';' or '!' or '?' or '\n';

    private sealed record LocalTtsSegmentAudio(int Index, string Text, string OutputFile);

    private static string Expand(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(expanded) || Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        return Path.Combine(AppContext.BaseDirectory, expanded);
    }

    private void EnsureLocalTtsDependencies(string executable, string model)
    {
        if (!_config.ProviderId.Equals("local-vits-tts", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var exeDirectory = Path.GetDirectoryName(executable) ?? "";
        var runtimeDll = Path.Combine(exeDirectory, "onnxruntime.dll");
        if (!File.Exists(runtimeDll))
        {
            throw new FileNotFoundException("本地 VITS 缺少 onnxruntime.dll。请确认便携版 Tools\\VITS\\bin 目录完整复制，不能只复制 sherpa-onnx-offline-tts.exe。", runtimeDll);
        }

        var modelDirectory = Path.GetDirectoryName(model) ?? "";
        foreach (var fileName in new[] { "lexicon.txt", "tokens.txt" })
        {
            var path = Path.Combine(modelDirectory, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"本地 VITS 模型目录缺少 {fileName}。请确认 Tools\\VITS 下的模型目录完整复制。", path);
            }
        }
    }

    private LocalTtsProcessPaths ResolveLocalTtsProcessPaths(string executable, string model)
    {
        var processExecutable = ToProcessPath(executable, mustExist: true);
        var processModel = ToProcessPath(model, mustExist: true);
        if (!_config.ProviderId.Equals("local-vits-tts", StringComparison.OrdinalIgnoreCase) ||
            IsAsciiPath(processExecutable) && IsAsciiPath(processModel))
        {
            return new LocalTtsProcessPaths(processExecutable, processModel);
        }

        var bridged = CreateVitsAsciiBridge(executable, model);
        return new LocalTtsProcessPaths(
            ToProcessPath(bridged.ExecutablePath, mustExist: true),
            ToProcessPath(bridged.ModelPath, mustExist: true));
    }

    private static LocalTtsProcessPaths CreateVitsAsciiBridge(string executable, string model)
    {
        var modelDirectory = Path.GetDirectoryName(model) ?? "";
        var exeDirectory = Path.GetDirectoryName(executable) ?? "";
        var cacheRoot = GetAsciiCacheRoot();
        var cacheKey = HashText($"{exeDirectory}|{modelDirectory}|{Path.GetFileName(model)}")[..16];
        var targetRoot = Path.Combine(cacheRoot, cacheKey);
        var targetBin = Path.Combine(targetRoot, "bin");
        var targetModelDir = Path.Combine(targetRoot, "model");
        Directory.CreateDirectory(targetBin);
        Directory.CreateDirectory(targetModelDir);

        foreach (var fileName in new[] { "sherpa-onnx-offline-tts.exe", "onnxruntime.dll", "onnxruntime_providers_shared.dll" })
        {
            CopyIfChanged(Path.Combine(exeDirectory, fileName), Path.Combine(targetBin, fileName));
        }

        CopyIfChanged(model, Path.Combine(targetModelDir, Path.GetFileName(model)));
        foreach (var fileName in new[] { "lexicon.txt", "tokens.txt" })
        {
            CopyIfChanged(Path.Combine(modelDirectory, fileName), Path.Combine(targetModelDir, fileName));
        }

        foreach (var fileName in new[] { "phone.fst", "number.fst" })
        {
            var optionalSource = Path.Combine(modelDirectory, fileName);
            if (File.Exists(optionalSource))
            {
                CopyIfChanged(optionalSource, Path.Combine(targetModelDir, fileName));
            }
        }

        var bridgedExecutable = Path.Combine(targetBin, "sherpa-onnx-offline-tts.exe");
        var bridgedModel = Path.Combine(targetModelDir, Path.GetFileName(model));
        if (!IsAsciiPath(bridgedExecutable) || !IsAsciiPath(bridgedModel))
        {
            throw new InvalidOperationException($"本地 VITS 无法创建纯英文路径缓存：{targetRoot}。请把便携版放到纯英文路径，例如 D:\\KnowledgeBaseQaAgent。");
        }

        ProviderDiagnostics.Info($"Local VITS path bridge: sourceModel={model}, bridgedModel={bridgedModel}, sourceExe={executable}, bridgedExe={bridgedExecutable}");
        return new LocalTtsProcessPaths(bridgedExecutable, bridgedModel);
    }

    private static string GetAsciiCacheRoot()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "KnowledgeBaseQaAgent", "TtsCache"),
            Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "KnowledgeBaseQaAgentTtsCache"),
            Path.Combine(Path.GetTempPath(), "KnowledgeBaseQaAgentTtsCache")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probe, "ok", Encoding.ASCII);
                File.Delete(probe);
                var processPath = ToProcessPath(candidate, mustExist: true);
                if (IsAsciiPath(processPath))
                {
                    return processPath;
                }
            }
            catch
            {
                // Try the next cache location.
            }
        }

        throw new InvalidOperationException("无法创建本地 VITS 的纯英文路径缓存。请把便携版放到纯英文路径，例如 D:\\KnowledgeBaseQaAgent。");
    }

    private static void CopyIfChanged(string source, string destination)
    {
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("本地 VITS 依赖文件缺失，无法创建纯英文路径缓存。", source);
        }

        var sourceInfo = new FileInfo(source);
        var destinationInfo = new FileInfo(destination);
        if (destinationInfo.Exists &&
            destinationInfo.Length == sourceInfo.Length &&
            destinationInfo.LastWriteTimeUtc >= sourceInfo.LastWriteTimeUtc)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static string ToProcessPath(string path, bool mustExist)
    {
        var expanded = Expand(path);
        if (string.IsNullOrWhiteSpace(expanded))
        {
            return expanded;
        }

        if (IsAsciiPath(expanded))
        {
            return expanded;
        }

        var shortPath = GetShortPath(expanded, mustExist);
        return !string.IsNullOrWhiteSpace(shortPath) && IsAsciiPath(shortPath)
            ? shortPath
            : expanded;
    }

    private static string GetShortPath(string path, bool mustExist)
    {
        var target = path;
        var fileName = "";
        if (!mustExist && !File.Exists(target) && !Directory.Exists(target))
        {
            fileName = Path.GetFileName(target);
            target = Path.GetDirectoryName(target) ?? target;
        }

        if (!File.Exists(target) && !Directory.Exists(target))
        {
            return "";
        }

        var buffer = new StringBuilder(1024);
        var length = GetShortPathName(target, buffer, buffer.Capacity);
        if (length <= 0 || length >= buffer.Capacity)
        {
            return "";
        }

        var result = buffer.ToString();
        return string.IsNullOrWhiteSpace(fileName) ? result : Path.Combine(result, fileName);
    }

    private static bool IsAsciiPath(string path) =>
        path.All(character => character <= 127);

    private static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string NormalizeLocalTtsVoice(string model, string voice)
    {
        if (!_config.ProviderId.Equals("local-vits-tts", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(voice) ? "0" : voice;
        }

        var defaultSid = model.Contains("fanchen-C", StringComparison.OrdinalIgnoreCase) ? 14 : 0;
        var maxSid = model.Contains("fanchen-C", StringComparison.OrdinalIgnoreCase)
            ? 186
            : model.Contains("sherpa-onnx-vits-zh-ll", StringComparison.OrdinalIgnoreCase)
                ? 4
                : model.Contains("vits-melo-tts-zh_en", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("breeze2-vits-onnx", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : model.Contains("vits-zh-hf-theresa", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("vits-zh-hf-eula", StringComparison.OrdinalIgnoreCase)
                        ? 803
                        : int.MaxValue;
        if (!int.TryParse(voice, out var sid) || sid < 0 || sid > maxSid)
        {
            ProviderDiagnostics.Info($"Local VITS voice normalized: model={model}, selectedSid={voice}, appliedSid={defaultSid}, allowedRange=0-{maxSid}");
            return defaultSid.ToString(CultureInfo.InvariantCulture);
        }

        return sid.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatExitCode(int exitCode) =>
        $"0x{unchecked((uint)exitCode):X8}";

    private static string Limit(string value)
    {
        value = (value ?? "").Trim();
        return value.Length <= 2000 ? value : value[..2000] + "...";
    }

    private static string EscapeArgument(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);

    private sealed record LocalTtsProcessPaths(string ExecutablePath, string ModelPath);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temp file cleanup failure is non-fatal.
        }
    }
}

public sealed class WhisperLocalRecognizer : ISpeechRecognizer
{
    private readonly ProviderConfig _config;

    public WhisperLocalRecognizer(ProviderConfig config)
    {
        _config = config;
    }

    public string ProviderId => _config.ProviderId;

    public Task<string> RecognizeOnceAsync(CancellationToken cancellationToken)
    {
        throw new NotSupportedException("Whisper.net provider is registered for local ASR, but live microphone capture is not wired in this MVP. Use Windows Dictation for live voice input.");
    }
}

public sealed class UnsupportedSpeechRecognizer : ISpeechRecognizer
{
    private readonly string _message;

    public UnsupportedSpeechRecognizer(string providerId, string message)
    {
        ProviderId = providerId;
        _message = message;
    }

    public string ProviderId { get; }

    public Task<string> RecognizeOnceAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException(_message);
}

public sealed class UnsupportedAudioTranscriber : IAudioTranscriber
{
    private readonly string _message;

    public UnsupportedAudioTranscriber(string providerId, string message)
    {
        ProviderId = providerId;
        _message = message;
    }

    public string ProviderId { get; }

    public Task<string> TranscribeFileAsync(string audioPath, CancellationToken cancellationToken) =>
        throw new NotSupportedException(_message);
}
