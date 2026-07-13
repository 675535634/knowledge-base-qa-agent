using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using KnowledgeBaseQaAgent.Desktop.Models;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed record LlmProviderDefinition(
    string Code,
    string Name,
    string DefaultBaseUrl,
    IReadOnlyList<string> RecommendModels,
    Dictionary<string, string> DefaultOptions);

public sealed class LlmProviderCatalogService
{
    private static readonly IReadOnlyList<LlmProviderDefinition> ProviderDefinitions =
    [
        Compatible("aliyun", "阿里通义 / 百炼", "https://dashscope.aliyuncs.com/compatible-mode/v1", ["qwen-turbo", "qwen-plus", "qwen-max", "qwen-vl-max"]),
        Compatible("deepseek", "DeepSeek", "https://api.deepseek.com/v1", ["deepseek-chat", "deepseek-reasoner"]),
        Compatible("zhipu", "智谱 AI / GLM", "https://open.bigmodel.cn/api/paas/v4", ["glm-4", "glm-4-flash", "glm-4-air", "glm-4-plus"]),
        Compatible("moonshot", "月之暗面 / Kimi", "https://api.moonshot.cn/v1", ["moonshot-v1-8k", "moonshot-v1-32k", "moonshot-v1-128k"]),
        Compatible("baichuan", "百川 AI", "https://api.baichuan-ai.com/v1", ["Baichuan4", "Baichuan3-Turbo", "Baichuan3-Turbo-128k"]),
        Compatible("spark", "讯飞星火", "https://spark-api-open.xf-yun.com/v1", ["spark-lite", "spark-pro", "spark-max", "spark-ultra"]),
        Compatible("yi", "零一万物 / 01.AI", "https://api.lingyiwanwu.com/v1", ["yi-lightning", "yi-large", "yi-medium", "yi-spark"]),
        Compatible("silicon", "硅基流动 SiliconFlow", "https://api.siliconflow.cn/v1", ["deepseek-ai/DeepSeek-V3", "deepseek-ai/DeepSeek-R1", "Qwen/Qwen2.5-72B-Instruct"]),
        Compatible("doubao", "豆包 / 火山方舟", "https://ark.cn-beijing.volces.com/api/v3", ["Doubao-pro-256k", "Doubao-pro-32k", "Doubao-lite-32k"]),
        Compatible("infini", "无问芯穹 Infini", "https://cloud.infini-ai.com/maas/v1", ["deepseek-r1", "deepseek-v3"]),
        Compatible("ppio", "PPIO 派欧云", "https://api.ppinfra.com/v3/openai", ["deepseek/deepseek-r1/community", "deepseek/deepseek-v3/community"]),
        Compatible("openai", "OpenAI", "https://api.openai.com/v1", ["gpt-4o", "gpt-4o-mini", "gpt-4.1-mini", "o3-mini"]),
        Compatible("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", ["openai/gpt-4o", "deepseek/deepseek-chat", "google/gemini-2.0-flash-001"]),
        Compatible("minimax", "MiniMax 中国", "https://api.minimaxi.com/v1", []),
        Compatible("minimax-global", "MiniMax Global", "https://api.minimax.io/v1", []),
        Compatible("groq", "Groq", "https://api.groq.com/openai/v1", []),
        Compatible("mistral", "Mistral", "https://api.mistral.ai/v1", []),
        Compatible("together", "Together AI", "https://api.together.xyz/v1", []),
        Compatible("fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1", []),
        Compatible("perplexity", "Perplexity", "https://api.perplexity.ai", []),
        Compatible("xai", "xAI / Grok", "https://api.x.ai/v1", []),
        Compatible("cerebras", "Cerebras", "https://api.cerebras.ai/v1", []),
        Compatible("sambanova", "SambaNova", "https://api.sambanova.ai/v1", []),
        Compatible("huggingface", "Hugging Face Router", "https://router.huggingface.co/v1", []),
        Compatible("modelscope", "ModelScope", "https://api-inference.modelscope.cn/v1", []),
        Compatible("nvidia", "NVIDIA NIM", "https://integrate.api.nvidia.com/v1", []),
        Compatible("novita", "Novita AI", "https://api.novita.ai/v3/openai", []),
        Compatible("hyperbolic", "Hyperbolic", "https://api.hyperbolic.xyz/v1", []),
        Compatible("hunyuan", "腾讯混元", "https://api.hunyuan.cloud.tencent.com/v1", []),
        Compatible("tencent-cloud-ti", "腾讯云 TI / LKEAP", "https://api.lkeap.cloud.tencent.com/v1", []),
        Compatible("baidu-cloud", "百度千帆", "https://qianfan.baidubce.com/v2", []),
        Compatible("stepfun", "阶跃星辰", "https://api.stepfun.com/v1", []),
        Compatible("longcat", "美团 LongCat", "https://api.longcat.chat/openai", []),
        Compatible("mimo", "小米 MiMo", "https://api.xiaomimimo.com/v1", []),
        Compatible("cohere", "Cohere Compatible", "https://api.cohere.com/compatibility/v1", []),
        Compatible("upstage", "Upstage Solar", "https://api.upstage.ai/v1/solar", []),
        Compatible("aihubmix", "AiHubMix", "https://aihubmix.com/v1", []),
        Compatible("302ai", "302.AI", "https://api.302.ai/v1", []),
        Compatible("qiniu", "七牛云 AI", "https://api.qnaigc.com/v1", []),
        Compatible("gitee-ai", "Gitee AI", "https://ai.gitee.com/v1", []),
        Compatible("zai", "Z.AI", "https://api.z.ai/api/paas/v4", []),
        Compatible("new-api", "New API / One API", "http://localhost:3000/v1", [], NoAuthOptions()),
        Compatible("lmstudio", "LM Studio", "http://localhost:1234/v1", [], NoAuthOptions()),
        Compatible("localai", "LocalAI", "http://localhost:8080/v1", [], NoAuthOptions()),
        Compatible("xinference", "Xinference", "http://localhost:9997/v1", [], NoAuthOptions()),
        Compatible("ollama", "Ollama", "http://localhost:11434/v1", [], new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["modelsPath"] = "/api/tags",
            ["modelsPathMode"] = "origin",
            ["modelsAuthRequired"] = "false"
        }),
        Compatible("custom", "自定义 OpenAI-compatible", "", ["deepseek-chat", "glm-4", "moonshot-v1-8k"])
    ];

    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient;

    public LlmProviderCatalogService(ICredentialService credentialService, HttpClient? httpClient = null)
    {
        _credentialService = credentialService;
        _httpClient = httpClient ?? new HttpClient();
    }

    public IReadOnlyList<LlmProviderDefinition> Providers => ProviderDefinitions;

    public LlmProviderDefinition GetProvider(string code) =>
        ProviderDefinitions.FirstOrDefault(provider => provider.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
        ?? ProviderDefinitions.First(provider => provider.Code == "custom");

    public async Task<IReadOnlyList<string>> FetchModelsAsync(
        string providerCode,
        string baseUrl,
        string apiKey,
        Dictionary<string, string> options,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProvider(providerCode);
        var effectiveOptions = BuildEffectiveOptions(provider, options);

        var effectiveBaseUrl = ResolveBaseUrlForProvider(provider.Code, baseUrl);
        if (string.IsNullOrWhiteSpace(effectiveBaseUrl))
        {
            throw new InvalidOperationException("请先配置 Base URL。");
        }

        var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? _credentialService.ReadSecret(CredentialName(providerCode))
            : apiKey.Trim();
        var authRequired = !effectiveOptions.GetValueOrDefault("modelsAuthRequired", "true")
            .Equals("false", StringComparison.OrdinalIgnoreCase);
        if (authRequired && string.IsNullOrWhiteSpace(effectiveApiKey))
        {
            throw new InvalidOperationException("请先填写 API Key。");
        }

        if (effectiveOptions.GetValueOrDefault("modelsMode", "auto").Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var method = effectiveOptions.GetValueOrDefault("modelsMethod", "GET").ToUpperInvariant();
        var url = BuildModelsEndpoint(effectiveBaseUrl, effectiveOptions);
        if (!string.IsNullOrWhiteSpace(effectiveApiKey) &&
            effectiveOptions.TryGetValue("apiKeyQuery", out var apiKeyQuery) &&
            !string.IsNullOrWhiteSpace(apiKeyQuery))
        {
            url = AppendQueryParameter(url, apiKeyQuery, effectiveApiKey);
        }

        using var request = new HttpRequestMessage(method == "POST" ? HttpMethod.Post : HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(effectiveApiKey) && string.IsNullOrWhiteSpace(effectiveOptions.GetValueOrDefault("apiKeyQuery", "")))
        {
            AddAuthHeaders(request, effectiveApiKey, effectiveOptions);
        }

        AddConfiguredHeaders(request, effectiveOptions);
        if (method == "POST")
        {
            var body = effectiveOptions.GetValueOrDefault("modelsBody", "");
            request.Content = string.IsNullOrWhiteSpace(body)
                ? JsonContent.Create(new { })
                : new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderHttpException("models/list", provider.Code, provider.Name, url, response.StatusCode, responseBody);
        }

        return ParseModelsResponse(responseBody, effectiveOptions);
    }

    public static Dictionary<string, string> BuildEffectiveOptions(
        LlmProviderDefinition provider,
        IReadOnlyDictionary<string, string> configuredOptions)
    {
        var effective = new Dictionary<string, string>(provider.DefaultOptions, StringComparer.OrdinalIgnoreCase);
        var configuredProvider = configuredOptions.GetValueOrDefault("llmProviderCode", "");
        var mayReuseProviderOptions = provider.Code.Equals("custom", StringComparison.OrdinalIgnoreCase) ||
            configuredProvider.Equals(provider.Code, StringComparison.OrdinalIgnoreCase);
        if (!mayReuseProviderOptions)
        {
            return effective;
        }

        foreach (var (key, value) in configuredOptions)
        {
            if (!IsProviderStateKey(key))
            {
                effective[key] = value;
            }
        }

        return effective;
    }

    public static ProviderConfig ApplyProviderToChatConfig(
        ProviderConfig config,
        string providerCode,
        string baseUrl,
        string model,
        IReadOnlyList<string> dynamicModels,
        bool replaceDynamicModels = true)
    {
        var previousProviderCode = config.Options.GetValueOrDefault("llmProviderCode", "");
        if (!string.IsNullOrWhiteSpace(previousProviderCode))
        {
            config.Options[ProviderBaseUrlKey(previousProviderCode)] = config.Options.GetValueOrDefault("baseUrl", "");
            config.Options[ProviderSelectedModelKey(previousProviderCode)] = config.Model;
        }

        var provider = ProviderDefinitions.FirstOrDefault(item => item.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase))
            ?? ProviderDefinitions.First(item => item.Code == "custom");
        var effectiveBaseUrl = ResolveBaseUrlForProvider(
            provider.Code,
            string.IsNullOrWhiteSpace(baseUrl) ? config.Options.GetValueOrDefault(ProviderBaseUrlKey(provider.Code), "") : baseUrl);
        config.ProviderId = "openai-chat";
        config.DisplayName = $"{provider.Name} Chat";
        config.Model = model;
        config.AuthRef = $"llm-{providerCode}";
        config.Options["llmProviderCode"] = providerCode;
        config.Options["baseUrl"] = effectiveBaseUrl;
        config.Options[ProviderBaseUrlKey(provider.Code)] = effectiveBaseUrl;
        config.Options[ProviderSelectedModelKey(provider.Code)] = model;
        foreach (var (key, value) in provider.DefaultOptions)
        {
            if (provider.Code.Equals("custom", StringComparison.OrdinalIgnoreCase))
            {
                config.Options.TryAdd(key, value);
            }
            else
            {
                config.Options[key] = value;
            }
        }

        config.Endpoint = JoinEndpoint(config.Options["baseUrl"], config.Options.GetValueOrDefault("chatPath", "/v1/chat/completions"));
        if (replaceDynamicModels)
        {
            ReplaceCachedModels(config, provider.Code, dynamicModels);
        }

        return config;
    }

    public static string CredentialName(string providerCode) => $"KnowledgeBaseQaAgent/llm-{providerCode}";

    public static string BuildCompatibleEndpoint(string baseUrl, string path) => JoinEndpoint(baseUrl, path);

    public static string ResolveBaseUrlForProvider(string providerCode, string? configuredBaseUrl)
    {
        var provider = ProviderDefinitions.FirstOrDefault(item => item.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase))
            ?? ProviderDefinitions.First(item => item.Code == "custom");
        var candidate = (configuredBaseUrl ?? "").Trim().TrimEnd('/');
        if (provider.Code.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return provider.DefaultBaseUrl;
        }

        return IsKnownDifferentProviderDefaultBaseUrl(provider.Code, candidate)
            ? provider.DefaultBaseUrl
            : candidate;
    }

    public static IReadOnlyList<string> GetCachedModels(ProviderConfig config, LlmProviderDefinition provider)
    {
        var models = new List<string>();
        var hasLiveCache = config.Options.GetValueOrDefault(DynamicModelsFetchedKey(provider.Code), "false")
            .Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!hasLiveCache)
        {
            models.AddRange(provider.RecommendModels);
        }

        models.AddRange(GetDiscoveredModels(config, provider.Code));

        var configuredProvider = config.Options.GetValueOrDefault("llmProviderCode", "");
        if (!hasLiveCache && configuredProvider.Equals(provider.Code, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(config.Model))
        {
            models.Add(config.Model);
        }

        return models
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> GetDiscoveredModels(ProviderConfig config, string providerCode)
    {
        var key = DynamicModelsKey(providerCode);
        var serialized = config.Options.GetValueOrDefault(key, "");
        if (string.IsNullOrWhiteSpace(serialized) &&
            config.Options.GetValueOrDefault("dynamicModelsProviderCode", "").Equals(providerCode, StringComparison.OrdinalIgnoreCase))
        {
            serialized = config.Options.GetValueOrDefault("dynamicModels", "");
        }

        return SplitModels(serialized);
    }

    public static string GetSavedBaseUrl(ProviderConfig config, LlmProviderDefinition provider)
    {
        var saved = config.Options.GetValueOrDefault(ProviderBaseUrlKey(provider.Code), "");
        if (string.IsNullOrWhiteSpace(saved) &&
            config.Options.GetValueOrDefault("llmProviderCode", "").Equals(provider.Code, StringComparison.OrdinalIgnoreCase))
        {
            saved = config.Options.GetValueOrDefault("baseUrl", "");
        }

        return ResolveBaseUrlForProvider(provider.Code, saved);
    }

    public static string GetSavedSelectedModel(ProviderConfig config, LlmProviderDefinition provider)
    {
        var saved = config.Options.GetValueOrDefault(ProviderSelectedModelKey(provider.Code), "");
        if (!string.IsNullOrWhiteSpace(saved))
        {
            return saved;
        }

        return config.Options.GetValueOrDefault("llmProviderCode", "").Equals(provider.Code, StringComparison.OrdinalIgnoreCase)
            ? config.Model
            : provider.RecommendModels.FirstOrDefault() ?? "";
    }

    public static void ReplaceCachedModels(ProviderConfig config, string providerCode, IReadOnlyList<string> models)
    {
        var serialized = string.Join("\n", models
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));
        config.Options[DynamicModelsKey(providerCode)] = serialized;
        config.Options[DynamicModelsFetchedKey(providerCode)] = "true";
        config.Options["dynamicModels"] = serialized;
        config.Options["dynamicModelsProviderCode"] = providerCode;
    }

    private static string DynamicModelsKey(string providerCode) => $"dynamicModels:{providerCode.Trim().ToLowerInvariant()}";
    private static string DynamicModelsFetchedKey(string providerCode) => $"dynamicModelsFetched:{providerCode.Trim().ToLowerInvariant()}";
    private static string ProviderBaseUrlKey(string providerCode) => $"providerBaseUrl:{providerCode.Trim().ToLowerInvariant()}";
    private static string ProviderSelectedModelKey(string providerCode) => $"providerSelectedModel:{providerCode.Trim().ToLowerInvariant()}";

    private static IReadOnlyList<string> SplitModels(string serialized) => serialized
        .Split(["\n", "\r\n", ",", "，"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static LlmProviderDefinition Compatible(
        string code,
        string name,
        string baseUrl,
        IReadOnlyList<string> models,
        Dictionary<string, string>? optionOverrides = null)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chatPath"] = "/v1/chat/completions",
            ["chatMethod"] = "POST",
            ["modelsPath"] = "/v1/models",
            ["modelsMethod"] = "GET",
            ["modelsMode"] = "auto",
            ["apiKeyHeader"] = "Authorization",
            ["apiKeyPrefix"] = "Bearer ",
            ["enableThinking"] = "false"
        };
        if (optionOverrides is not null)
        {
            foreach (var (key, value) in optionOverrides)
            {
                options[key] = value;
            }
        }

        return new(code, name, baseUrl, models, options);
    }

    private static Dictionary<string, string> NoAuthOptions() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["modelsAuthRequired"] = "false"
    };

    private static bool IsKnownDifferentProviderDefaultBaseUrl(string providerCode, string baseUrl)
    {
        foreach (var provider in ProviderDefinitions)
        {
            if (provider.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase) ||
                provider.Code.Equals("custom", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(provider.DefaultBaseUrl))
            {
                continue;
            }

            if (BaseUrlsEqual(baseUrl, provider.DefaultBaseUrl))
            {
                return true;
            }
        }

        return false;
    }

    private static bool BaseUrlsEqual(string left, string right)
    {
        var normalizedLeft = NormalizeBaseUrlForComparison(left);
        var normalizedRight = NormalizeBaseUrlForComparison(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft) &&
            normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBaseUrlForComparison(string value)
    {
        var trimmed = (value ?? "").Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "";
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
        }

        return trimmed;
    }

    private static void AddAuthHeaders(HttpRequestMessage request, string apiKey, Dictionary<string, string> options)
    {
        var header = options.GetValueOrDefault("apiKeyHeader", "Authorization").Trim();
        var prefix = options.GetValueOrDefault("apiKeyPrefix", "Bearer ");
        if (header.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                request.Headers.TryAddWithoutValidation(header, apiKey);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(prefix.Trim(), apiKey);
            }
        }
        else if (!string.IsNullOrWhiteSpace(header))
        {
            request.Headers.TryAddWithoutValidation(header, $"{prefix}{apiKey}");
        }
    }

    private static void AddConfiguredHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string> options)
    {
        if (!options.TryGetValue("modelsHeadersJson", out var headersJson) || string.IsNullOrWhiteSpace(headersJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(headersJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("modelsHeadersJson 必须是 JSON 对象。");
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                request.Headers.TryAddWithoutValidation(property.Name, property.Value.GetString());
            }
        }
    }

    private static string BuildModelsEndpoint(string baseUrl, IReadOnlyDictionary<string, string> options)
    {
        var path = options.GetValueOrDefault("modelsPath", "/v1/models");
        if (Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            return path;
        }

        if (options.GetValueOrDefault("modelsPathMode", "base").Equals("origin", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return new Uri(new Uri(baseUri.GetLeftPart(UriPartial.Authority)), path.TrimStart('/')).ToString();
        }

        return JoinEndpoint(baseUrl, path);
    }

    private static string AppendQueryParameter(string url, string name, string value)
    {
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }

    private static bool IsProviderStateKey(string key) =>
        key.Equals("baseUrl", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("dynamicModels", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("dynamicModelsProviderCode", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("dynamicModels:", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("dynamicModelsFetched:", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("providerBaseUrl:", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("providerSelectedModel:", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("llmProviderCode", StringComparison.OrdinalIgnoreCase);

    private static string JoinEndpoint(string baseUrl, string path)
    {
        var left = (baseUrl ?? "").Trim().TrimEnd('/');
        var normalizedPath = NormalizeEndpointPath(left, path);
        var right = (normalizedPath ?? "").Trim().TrimStart('/');
        return string.IsNullOrWhiteSpace(left) ? $"/{right}" : $"{left}/{right}";
    }

    private static string NormalizeEndpointPath(string baseUrl, string path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? "" : path.Trim();
        if (string.IsNullOrWhiteSpace(baseUrl) || !normalizedPath.StartsWith('/'))
        {
            return normalizedPath;
        }

        try
        {
            var basePath = new Uri(baseUrl).AbsolutePath.TrimEnd('/');
            var firstSegment = normalizedPath.TrimStart('/').Split('/').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstSegment) && basePath.EndsWith($"/{firstSegment}", StringComparison.OrdinalIgnoreCase))
            {
                return "/" + string.Join("/", normalizedPath.TrimStart('/').Split('/').Skip(1));
            }
        }
        catch
        {
            // Keep the configured path when Base URL is not absolute.
        }

        return normalizedPath;
    }

    public static IReadOnlyList<string> ParseModelsResponse(
        string responseBody,
        IReadOnlyDictionary<string, string>? options = null)
    {
        using var document = JsonDocument.Parse(responseBody);
        var result = new List<string>();
        AddModels(document.RootElement, result, options);
        var stripPrefix = options?.GetValueOrDefault("modelsStripPrefix", "") ?? "";
        return result
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => StripPrefix(model.Trim(), stripPrefix))
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddModels(
        JsonElement element,
        List<string> result,
        IReadOnlyDictionary<string, string>? options)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AddModel(item, result, options);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var key in new[] { "data", "models", "items", "results" })
        {
            if (element.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                AddModels(array, result, options);
            }
        }
    }

    private static void AddModel(
        JsonElement item,
        List<string> result,
        IReadOnlyDictionary<string, string>? options)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            result.Add(item.GetString() ?? "");
            return;
        }

        if (item.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!IsAvailableModel(item) || !SupportsRequiredCapability(item, options))
        {
            return;
        }

        foreach (var key in new[] { "id", "model", "model_id", "name", "baseModelId" })
        {
            if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                result.Add(value.GetString() ?? "");
                return;
            }
        }
    }

    private static bool IsAvailableModel(JsonElement item)
    {
        foreach (var key in new[] { "archived", "deleted", "disabled" })
        {
            if (item.TryGetProperty(key, out var flag) && flag.ValueKind == JsonValueKind.True)
            {
                return false;
            }
        }

        if (item.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        if (item.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
        {
            return status.GetString()?.ToLowerInvariant() is not ("archived" or "deleted" or "disabled" or "unavailable" or "failed");
        }

        return true;
    }

    private static bool SupportsRequiredCapability(
        JsonElement item,
        IReadOnlyDictionary<string, string>? options)
    {
        var required = options?.GetValueOrDefault("modelsRequiredCapability", "") ?? "";
        if (string.IsNullOrWhiteSpace(required) ||
            !item.TryGetProperty("supportedGenerationMethods", out var methods) ||
            methods.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        return methods.EnumerateArray().Any(method =>
            method.ValueKind == JsonValueKind.String &&
            required.Equals(method.GetString(), StringComparison.OrdinalIgnoreCase));
    }

    private static string StripPrefix(string value, string prefix) =>
        !string.IsNullOrWhiteSpace(prefix) && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
}
