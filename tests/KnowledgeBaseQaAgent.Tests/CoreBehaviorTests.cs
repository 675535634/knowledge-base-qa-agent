using KnowledgeBaseQaAgent.Desktop.Models;
using KnowledgeBaseQaAgent.Desktop.Services;

namespace KnowledgeBaseQaAgent.Tests;

public sealed class CoreBehaviorTests
{
    [Fact]
    public void ChunkerSplitsLongText()
    {
        var text = string.Join("。", Enumerable.Range(0, 180).Select(i => $"第{i}段知识库内容用于检索测试"));
        var document = new ParsedDocument("sample.txt", "sample", [new ParsedSection(text, "text")]);

        var chunks = new TextChunker().Chunk(document);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk.Text)));
        Assert.Equal(Enumerable.Range(0, chunks.Count), chunks.Select(chunk => chunk.Ordinal));
    }

    [Fact]
    public async Task SettingsServiceCreatesProviderDefaults()
    {
        var root = NewTempRoot();
        var service = new SettingsService(new AppPaths(root));

        var settings = await service.LoadAsync();

        Assert.Contains(settings.Providers, provider => provider.ProviderId == "openai-chat");
        Assert.Contains(settings.Providers, provider => provider.ProviderId == "local-hash-embedding");
        var chatProvider = settings.Providers.First(provider => provider.ProviderId == "openai-chat");
        Assert.Equal("aliyun", chatProvider.Options["llmProviderCode"]);
        Assert.Equal("qwen-plus", chatProvider.Model);
        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", chatProvider.Endpoint);
        Assert.Equal(AppSettings.DefaultAdminPinHash, settings.AdminPinHash);
        Assert.NotEmpty(settings.QuickQuestions);
        Assert.NotEmpty(settings.WakeWords);
        Assert.NotEmpty(settings.AssistantTags);
        Assert.False(string.IsNullOrWhiteSpace(settings.AssistantName));
        Assert.Equal(AppSettings.DefaultAssistantName, settings.AssistantName);
        Assert.False(string.IsNullOrWhiteSpace(settings.EndSessionButtonText));
        Assert.False(string.IsNullOrWhiteSpace(settings.PetHintText));
        Assert.False(string.IsNullOrWhiteSpace(settings.VoiceButtonText));
        Assert.False(string.IsNullOrWhiteSpace(settings.GreetingText));
        Assert.False(string.IsNullOrWhiteSpace(settings.SystemPrompt));
        Assert.False(string.IsNullOrWhiteSpace(settings.CharacterPrompt));
        Assert.NotEmpty(settings.WorldBookEntries);
        Assert.Equal(12, settings.RetrievalTopK);
        Assert.Equal(AppSettings.DefaultPetImagePath, settings.PetImagePath);
        Assert.Equal(AppSettings.DefaultLogoImagePath, settings.LogoImagePath);
        Assert.True(settings.PetFrameIntervalMs >= 120);
        Assert.True(File.Exists(Path.Combine(root, "settings.json")));
    }

    [Fact]
    public void AppPathsUsePortableDataDirectoryWhenArgumentIsPresent()
    {
        var paths = AppPaths.FromStartupArgs(["--portable"]);
        var expectedRoot = Path.Combine(AppContext.BaseDirectory, "Data");

        Assert.True(paths.IsPortable);
        Assert.Equal(expectedRoot, paths.Root);
        Assert.Equal(Path.Combine(expectedRoot, "settings.json"), paths.SettingsPath);
        Assert.Equal(Path.Combine(expectedRoot, "secrets.json"), paths.SecretsPath);
        Assert.Equal(Path.Combine(expectedRoot, "secret.key"), paths.SecretKeyPath);
        Assert.Contains("便携模式", paths.ModeDescription);
    }

    [Fact]
    public void PortableCredentialServicePersistsSecretsInDataDirectory()
    {
        var root = NewTempRoot();
        var paths = new AppPaths(root, isPortable: true);
        var credentials = new PortableCredentialService(paths);

        credentials.WriteSecret("KnowledgeBaseQaAgent/openai-compatible", "sk-test");

        Assert.Equal("sk-test", credentials.ReadSecret("knowledgebaseqaagent/OPENAI-compatible"));
        Assert.True(File.Exists(Path.Combine(root, "secrets.json")));
        Assert.True(File.Exists(Path.Combine(root, "secret.key")));
        Assert.DoesNotContain("sk-test", File.ReadAllText(Path.Combine(root, "secrets.json")));
        Assert.Contains("Data", credentials.StorageDescription);
    }

    [Fact]
    public void PortableCredentialServiceReadsLegacyPlaintextSecrets()
    {
        var root = NewTempRoot();
        var paths = new AppPaths(root, isPortable: true);
        File.WriteAllText(paths.SecretsPath, """{"KnowledgeBaseQaAgent/openai-compatible":"sk-legacy"}""");

        var credentials = new PortableCredentialService(paths);

        Assert.Equal("sk-legacy", credentials.ReadSecret("KnowledgeBaseQaAgent/openai-compatible"));
    }

    [Fact]
    public async Task SqliteStorePersistsAndSearchesChunks()
    {
        var root = NewTempRoot();
        var store = new SqliteKnowledgeStore(new AppPaths(root));
        await store.InitializeAsync();
        var embedding = new LocalHashEmbeddingProvider();
        var documentId = await store.AddDocumentAsync("a.txt", "a", Guid.NewGuid().ToString("N"));
        var vector = await embedding.EmbedAsync("Windows WPF knowledge base assistant", CancellationToken.None);
        await store.AddChunkAsync(documentId, 0, "Windows WPF knowledge base assistant", "a.txt", "text", "hash", vector);

        var query = await embedding.EmbedAsync("WPF assistant", CancellationToken.None);
        var results = await store.SearchAsync(query, 3);

        Assert.NotEmpty(results);
        Assert.Equal("Windows WPF knowledge base assistant", results[0].Chunk.Text);
        Assert.True(results[0].Score > 0);

        await store.DeleteDocumentAsync(documentId);
        Assert.Empty(await store.GetDocumentsAsync());
        Assert.Empty(await store.SearchAsync(query, 3));
    }

    [Fact]
    public async Task ProviderRegistryBuildsPromptProfileWithTriggeredWorldBook()
    {
        var root = NewTempRoot();
        var paths = new AppPaths(root);
        var settingsService = new SettingsService(paths);
        var settings = await settingsService.LoadAsync();
        settings.WorldBookEntries =
        [
            new()
            {
                Name = "办理时间",
                Keywords = ["开放时间", "几点"],
                Content = "回答开放时间时必须引用知识库，不要猜测。"
            },
            new()
            {
                Name = "无关设定",
                Keywords = ["停车"],
                Content = "停车信息。"
            }
        ];
        var registry = new ProviderRegistry(settingsService, new InMemoryCredentialService(), settings);

        var profile = registry.CreatePromptProfile("请问开放时间是几点？");

        Assert.Contains(AppSettings.DefaultAssistantName, profile.CharacterPrompt);
        Assert.Single(profile.ActiveWorldBookEntries);
        Assert.Equal("办理时间", profile.ActiveWorldBookEntries[0].Name);
    }

    [Fact]
    public void LlmProviderCatalogAppliesProviderToChatConfig()
    {
        var config = new ProviderConfig { ProviderId = "openai-chat" };

        LlmProviderCatalogService.ApplyProviderToChatConfig(
            config,
            "deepseek",
            "",
            "deepseek-chat",
            ["deepseek-chat", "deepseek-reasoner"]);

        Assert.Equal("openai-chat", config.ProviderId);
        Assert.Equal("llm-deepseek", config.AuthRef);
        Assert.Equal("deepseek-chat", config.Model);
        Assert.Equal("deepseek", config.Options["llmProviderCode"]);
        Assert.Equal("https://api.deepseek.com/v1/chat/completions", config.Endpoint);
        Assert.Contains("deepseek-reasoner", config.Options["dynamicModels"]);
    }

    [Fact]
    public void LlmProviderCatalogCorrectsStaleKnownBaseUrl()
    {
        var config = new ProviderConfig { ProviderId = "openai-chat" };

        var resolved = LlmProviderCatalogService.ResolveBaseUrlForProvider("aliyun", "https://api.openai.com/v1");
        LlmProviderCatalogService.ApplyProviderToChatConfig(
            config,
            "aliyun",
            "https://api.openai.com/v1",
            "qwen-plus",
            []);

        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", resolved);
        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", config.Options["baseUrl"]);
        Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", config.Endpoint);
    }

    [Fact]
    public void ThinkingSwitchOnlyAppliesToSupportedModels()
    {
        Assert.True(OpenAiCompatibleChatProvider.ModelSupportsThinkingSwitch("qwen3.7-plus"));
        Assert.True(OpenAiCompatibleChatProvider.ModelSupportsThinkingSwitch("QwQ-32B"));
        Assert.True(OpenAiCompatibleChatProvider.ModelSupportsThinkingSwitch("qvq-max"));
        Assert.False(OpenAiCompatibleChatProvider.ModelSupportsThinkingSwitch("qwen-plus"));
        Assert.False(OpenAiCompatibleChatProvider.ModelSupportsThinkingSwitch("gpt-4o-mini"));
    }

    [Fact]
    public void BroadInventoryQuestionsExpandRetrievalLimit()
    {
        Assert.Equal(5, RagQueryClassifier.ResolveRetrievalLimit("学校地址在哪里？", 5));
        Assert.Equal(24, RagQueryClassifier.ResolveRetrievalLimit("学校所有专业有哪些？", 5));
        Assert.True(RagQueryClassifier.IsBroadInventoryQuestion("请列出全部专业清单"));
        Assert.False(RagQueryClassifier.IsBroadInventoryQuestion("美容专业学什么？"));
    }

    [Fact]
    public async Task RagPipelineImportsTextAndAnswersWithLocalProviders()
    {
        var root = NewTempRoot();
        var file = Path.Combine(root, "kb.txt");
        await File.WriteAllTextAsync(file, "桌宠窗口使用 WPF 透明置顶窗口实现。知识库问答通过本地 SQLite 保存 chunk。");
        var paths = new AppPaths(root);
        var settingsService = new SettingsService(paths);
        var settings = await settingsService.LoadAsync();
        settings.ChatProviderId = "local-context-chat";
        settings.EmbeddingProviderId = "local-hash-embedding";
        var registry = new ProviderRegistry(settingsService, new InMemoryCredentialService(), settings);
        var store = new SqliteKnowledgeStore(paths);
        await store.InitializeAsync();
        var rag = new RagService(store, new DocumentParser(), new TextChunker(), registry);

        var imported = await rag.ImportDocumentAsync(file);
        var answer = await rag.AskAsync("桌宠窗口如何实现？", 3);

        Assert.True(imported > 0);
        Assert.Contains("WPF", answer.Answer);
        Assert.NotEmpty(answer.Citations);
    }

    private static string NewTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "kbqa-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class InMemoryCredentialService : ICredentialService
    {
        private readonly Dictionary<string, string> _secrets = new();

        public string StorageDescription => "in-memory test credentials";
        public string? ReadSecret(string targetName) => _secrets.GetValueOrDefault(targetName);
        public void WriteSecret(string targetName, string secret) => _secrets[targetName] = secret;
        public void DeleteSecret(string targetName) => _secrets.Remove(targetName);
    }
}
