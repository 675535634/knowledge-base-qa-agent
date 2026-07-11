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
        Compatible("custom", "自定义 OpenAI-compatible", "", ["deepseek-chat", "glm-4", "moonshot-v1-8k"])
    ];

    private readonly ICredentialService _credentialService;
    private readonly HttpClient _httpClient = new();

    public LlmProviderCatalogService(ICredentialService credentialService)
    {
        _credentialService = credentialService;
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
        var effectiveOptions = new Dictionary<string, string>(provider.DefaultOptions, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in options)
        {
            effectiveOptions[key] = value;
        }

        var effectiveBaseUrl = ResolveBaseUrlForProvider(provider.Code, baseUrl);
        if (string.IsNullOrWhiteSpace(effectiveBaseUrl))
        {
            throw new InvalidOperationException("请先配置 Base URL。");
        }

        var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey)
            ? _credentialService.ReadSecret(CredentialName(providerCode))
            : apiKey.Trim();
        if (string.IsNullOrWhiteSpace(effectiveApiKey))
        {
            throw new InvalidOperationException("请先填写 API Key。");
        }

        if (effectiveOptions.GetValueOrDefault("modelsMode", "auto").Equals("disable", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var method = effectiveOptions.GetValueOrDefault("modelsMethod", "GET").ToUpperInvariant();
        var url = JoinEndpoint(effectiveBaseUrl, effectiveOptions.GetValueOrDefault("modelsPath", "/v1/models"));
        using var request = new HttpRequestMessage(method == "POST" ? HttpMethod.Post : HttpMethod.Get, url);
        AddAuthHeaders(request, effectiveApiKey, effectiveOptions);
        if (method == "POST")
        {
            var body = effectiveOptions.GetValueOrDefault("modelsBody", "");
            request.Content = string.IsNullOrWhiteSpace(body)
                ? JsonContent.Create(new { })
                : new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderHttpException("models/list", provider.Code, provider.Name, url, response.StatusCode, responseBody);
        }

        return ExtractModels(responseBody);
    }

    public static ProviderConfig ApplyProviderToChatConfig(
        ProviderConfig config,
        string providerCode,
        string baseUrl,
        string model,
        IReadOnlyList<string> dynamicModels)
    {
        var provider = ProviderDefinitions.FirstOrDefault(item => item.Code.Equals(providerCode, StringComparison.OrdinalIgnoreCase))
            ?? ProviderDefinitions.First(item => item.Code == "custom");
        config.ProviderId = "openai-chat";
        config.DisplayName = $"{provider.Name} Chat";
        config.Model = model;
        config.AuthRef = $"llm-{providerCode}";
        config.Options["llmProviderCode"] = providerCode;
        config.Options["baseUrl"] = ResolveBaseUrlForProvider(provider.Code, baseUrl);
        foreach (var (key, value) in provider.DefaultOptions)
        {
            config.Options.TryAdd(key, value);
        }

        config.Endpoint = JoinEndpoint(config.Options["baseUrl"], config.Options.GetValueOrDefault("chatPath", "/v1/chat/completions"));
        if (dynamicModels.Count > 0)
        {
            config.Options["dynamicModels"] = string.Join("\n", dynamicModels.Distinct(StringComparer.OrdinalIgnoreCase));
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
        models.AddRange(provider.RecommendModels);
        if (config.Options.TryGetValue("dynamicModels", out var dynamicModels))
        {
            models.AddRange(dynamicModels.Split(["\n", "\r\n", ",", "，"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (!string.IsNullOrWhiteSpace(config.Model))
        {
            models.Add(config.Model);
        }

        return models
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static LlmProviderDefinition Compatible(string code, string name, string baseUrl, IReadOnlyList<string> models) =>
        new(code, name, baseUrl, models, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chatPath"] = "/v1/chat/completions",
            ["chatMethod"] = "POST",
            ["modelsPath"] = "/v1/models",
            ["modelsMethod"] = "GET",
            ["modelsMode"] = "auto",
            ["apiKeyHeader"] = "Authorization",
            ["apiKeyPrefix"] = "Bearer ",
            ["enableThinking"] = "false"
        });

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

    private static IReadOnlyList<string> ExtractModels(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var result = new List<string>();
        AddModels(document.RootElement, result);
        return result
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddModels(JsonElement element, List<string> result)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                AddModel(item, result);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var key in new[] { "data", "models", "items" })
        {
            if (element.TryGetProperty(key, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                AddModels(array, result);
            }
        }
    }

    private static void AddModel(JsonElement item, List<string> result)
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

        foreach (var key in new[] { "id", "model", "model_id", "name" })
        {
            if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                result.Add(value.GetString() ?? "");
                return;
            }
        }
    }
}
