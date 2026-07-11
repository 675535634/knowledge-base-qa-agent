using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KnowledgeBaseQaAgent.Desktop.Models;
using KnowledgeBaseQaAgent.Desktop.Services;
using Microsoft.Win32;

namespace KnowledgeBaseQaAgent.Desktop.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const string MaskedApiKey = "********";

    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly SettingsService _settingsService;
    private readonly ICredentialService _credentialService;
    private readonly ProviderRegistry _providers;
    private readonly SqliteKnowledgeStore _store;
    private readonly RagService _rag;
    private readonly SpeechCoordinator _speech;
    private readonly LlmProviderCatalogService _llmProviderCatalog;
    private readonly AvatarStateService _avatar;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _loadingLlmProviderForm;
    private bool _initializingRuntimeProviderForm = true;

    [ObservableProperty]
    private string question = "";

    [ObservableProperty]
    private string status = "Ready";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VoiceAskCommand))]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    private bool isVoiceAskBusy;

    [ObservableProperty]
    private string voiceInteractionHint = "点击语音提问后，请直接说出完整问题，约 6 秒后自动识别。";

    [ObservableProperty]
    private string apiKeyToSave = "";

    [ObservableProperty]
    private string llmApiKeyToSave = "";

    [ObservableProperty]
    private string llmSelectedProviderCode = "";

    [ObservableProperty]
    private string llmBaseUrl = "";

    [ObservableProperty]
    private string llmSelectedModel = "";

    [ObservableProperty]
    private bool llmEnableThinking;

    [ObservableProperty]
    private string llmModelStatus = "";

    [ObservableProperty]
    private string embeddingModel = "";

    [ObservableProperty]
    private string asrModel = "";

    [ObservableProperty]
    private string asrTestAudioPath = "";

    [ObservableProperty]
    private string asrTestResult = "";

    [ObservableProperty]
    private string ttsModel = "";

    [ObservableProperty]
    private string ttsEndpoint = "";

    [ObservableProperty]
    private string ttsConfigHint = "";

    [ObservableProperty]
    private string ttsWorkspaceId = "";

    [ObservableProperty]
    private string ttsVoice = "";

    [ObservableProperty]
    private double ttsSpeed = 1.0;

    [ObservableProperty]
    private string ttsInstructions = "";

    [ObservableProperty]
    private bool ttsOptimizeInstructions;

    [ObservableProperty]
    private string ttsPreviewText = "您好，请问有什么需要了解的？我可以为您介绍学校专业、招生政策和校园服务。";

    [ObservableProperty]
    private string secretStorageDescription = "";

    [ObservableProperty]
    private string adminFooterText = "";

    [ObservableProperty]
    private string greetingText;

    [ObservableProperty]
    private string assistantName;

    [ObservableProperty]
    private string visitorWindowTitle;

    [ObservableProperty]
    private string visitorHeadline;

    [ObservableProperty]
    private string wakeHintText;

    [ObservableProperty]
    private string petHintText;

    [ObservableProperty]
    private string adminButtonText;

    [ObservableProperty]
    private string closeButtonText;

    [ObservableProperty]
    private string endSessionButtonText;

    [ObservableProperty]
    private string quickQuestionsHeader;

    [ObservableProperty]
    private string conversationHeader;

    [ObservableProperty]
    private string sendButtonText;

    [ObservableProperty]
    private string voiceButtonText;

    [ObservableProperty]
    private string idleStatusText;

    [ObservableProperty]
    private string listeningStatusText;

    [ObservableProperty]
    private string thinkingStatusText;

    [ObservableProperty]
    private string speakingStatusText;

    [ObservableProperty]
    private string errorStatusText;

    [ObservableProperty]
    private string petImagePath;

    [ObservableProperty]
    private string petFramesDirectory;

    [ObservableProperty]
    private int petFrameIntervalMs;

    [ObservableProperty]
    private string petPreviewImagePath;

    [ObservableProperty]
    private string logoImagePath;

    [ObservableProperty]
    private string logoPreviewImagePath;

    [ObservableProperty]
    private string quickQuestionsText;

    [ObservableProperty]
    private string assistantTagsText;

    [ObservableProperty]
    private string wakeWordsText;

    [ObservableProperty]
    private string systemPromptText;

    [ObservableProperty]
    private string characterPromptText;

    [ObservableProperty]
    private string worldBookText;

    [ObservableProperty]
    private string newAdminPin = "";

    [ObservableProperty]
    private int retrievalTopK;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedDocumentCommand))]
    private DocumentItem? selectedDocument;

    [ObservableProperty]
    private string selectedChatProviderId;

    [ObservableProperty]
    private string selectedEmbeddingProviderId;

    [ObservableProperty]
    private string selectedSpeechRecognizerId;

    [ObservableProperty]
    private string selectedSpeechSynthesizerId;

    public ObservableCollection<ConversationItem> Conversation { get; } = [];
    public ObservableCollection<DocumentItem> Documents { get; } = [];
    public ObservableCollection<ProviderConfig> ProviderConfigs { get; }
    public ObservableCollection<QuickQuestionItem> QuickQuestions { get; } = [];
    public ObservableCollection<AssistantTagItem> AssistantTags { get; } = [];
    public ObservableCollection<LogEntry> Logs { get; } = [];
    public ObservableCollection<ProviderOption> EmbeddingProviders { get; } = [];
    public ObservableCollection<ProviderOption> SpeechRecognizerProviders { get; } = [];
    public ObservableCollection<ProviderOption> SpeechSynthesizerProviders { get; } = [];
    public ObservableCollection<LlmProviderOptionView> LlmProviderOptions { get; } = [];
    public ObservableCollection<string> LlmModelOptions { get; } = [];
    public ObservableCollection<string> EmbeddingModelOptions { get; } = [];
    public ObservableCollection<string> AsrModelOptions { get; } = [];
    public ObservableCollection<string> TtsModelOptions { get; } = [];

    public event EventHandler<AppSettings>? SettingsApplied;

    public string VoiceAskButtonDisplayText => IsVoiceAskBusy ? "正在聆听..." : VoiceButtonText;

    public MainViewModel(
        AppSettings settings,
        AppPaths paths,
        SettingsService settingsService,
        ICredentialService credentialService,
        ProviderRegistry providers,
        SqliteKnowledgeStore store,
        RagService rag,
        SpeechCoordinator speech,
        LlmProviderCatalogService llmProviderCatalog,
        AvatarStateService avatar)
    {
        _settings = settings;
        _paths = paths;
        _settingsService = settingsService;
        _credentialService = credentialService;
        _providers = providers;
        _store = store;
        _rag = rag;
        _speech = speech;
        _llmProviderCatalog = llmProviderCatalog;
        _avatar = avatar;
        ProviderDiagnostics.Logged += OnProviderDiagnosticLogged;
        SecretStorageDescription = credentialService.StorageDescription;
        AdminFooterText = $"游客只通过桌宠进入触屏问答；本窗口仅管理员 PIN 验证后可见。API Key 保存到 {SecretStorageDescription}，不在游客界面展示。";
        RetrievalTopK = settings.RetrievalTopK;
        AssistantName = settings.AssistantName;
        VisitorWindowTitle = settings.VisitorWindowTitle;
        VisitorHeadline = settings.VisitorHeadline;
        GreetingText = settings.GreetingText;
        WakeHintText = settings.WakeHintText;
        PetHintText = settings.PetHintText;
        AdminButtonText = settings.AdminButtonText;
        CloseButtonText = settings.CloseButtonText;
        EndSessionButtonText = settings.EndSessionButtonText;
        QuickQuestionsHeader = settings.QuickQuestionsHeader;
        ConversationHeader = settings.ConversationHeader;
        SendButtonText = settings.SendButtonText;
        VoiceButtonText = settings.VoiceButtonText;
        IdleStatusText = settings.IdleStatusText;
        ListeningStatusText = settings.ListeningStatusText;
        ThinkingStatusText = settings.ThinkingStatusText;
        SpeakingStatusText = settings.SpeakingStatusText;
        ErrorStatusText = settings.ErrorStatusText;
        Status = IdleStatusText;
        PetImagePath = settings.PetImagePath;
        PetFramesDirectory = settings.PetFramesDirectory;
        PetFrameIntervalMs = settings.PetFrameIntervalMs;
        PetPreviewImagePath = ResolvePetPreviewImagePath(settings);
        LogoImagePath = settings.LogoImagePath;
        LogoPreviewImagePath = ResolveLogoPreviewImagePath(settings);
        QuickQuestionsText = string.Join(Environment.NewLine, settings.QuickQuestions);
        AssistantTagsText = string.Join(Environment.NewLine, settings.AssistantTags);
        WakeWordsText = string.Join(Environment.NewLine, settings.WakeWords);
        SystemPromptText = settings.SystemPrompt;
        CharacterPromptText = settings.CharacterPrompt;
        WorldBookText = FormatWorldBook(settings.WorldBookEntries);
        SelectedChatProviderId = settings.ChatProviderId;
        SelectedEmbeddingProviderId = settings.EmbeddingProviderId;
        SelectedSpeechRecognizerId = settings.SpeechRecognizerId;
        SelectedSpeechSynthesizerId = settings.SpeechSynthesizerId;
        ProviderConfigs = new ObservableCollection<ProviderConfig>(settings.Providers);
        RefreshQuickQuestions();
        RefreshAssistantTags();
        FillProviderOptions();
        FillLlmProviderOptions();
        LoadLlmProviderForm();
        LoadRuntimeCapabilityModels();
        _initializingRuntimeProviderForm = false;
        _ = InitializeAsync();
    }

    [RelayCommand]
    private async Task ImportDocumentsAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Knowledge files|*.pdf;*.docx;*.txt;*.md;*.markdown;*.html;*.htm|All files|*.*",
            Multiselect = true,
            Title = "导入知识库文档"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _avatar.Set(AvatarState.Thinking);
            foreach (var fileName in dialog.FileNames)
            {
                Status = $"正在导入 {Path.GetFileName(fileName)}";
                var count = await _rag.ImportDocumentAsync(fileName, _disposeCts.Token);
                AddLog("Info", count == 0
                    ? $"Skipped duplicate or empty document: {fileName}"
                    : $"Imported {count} chunks: {fileName}");
            }

            await RefreshDocumentsAsync();
            Status = IdleStatusText;
            _avatar.Set(AvatarState.Idle);
        }
        catch (Exception ex)
        {
            _avatar.Set(AvatarState.Error);
            AddLog("Error", ex.Message);
            Status = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedDocument))]
    private async Task DeleteSelectedDocumentAsync()
    {
        if (SelectedDocument is null)
        {
            return;
        }

        try
        {
            var title = SelectedDocument.Title;
            if (System.Windows.MessageBox.Show(
                    $"确认从知识库删除“{title}”？\n不会删除磁盘上的原始文件。",
                    "删除知识库文档",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            await _store.DeleteDocumentAsync(SelectedDocument.Id, _disposeCts.Token);
            SelectedDocument = null;
            await RefreshDocumentsAsync();
            Status = $"已删除知识库文档：{title}";
            AddLog("Info", Status);
        }
        catch (Exception ex)
        {
            _avatar.Set(AvatarState.Error);
            Status = ex.Message;
            AddLog("Error", $"Delete document failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ClearKnowledgeBaseAsync()
    {
        try
        {
            if (System.Windows.MessageBox.Show(
                    "确认清空知识库索引？\n这会删除所有已导入文档、chunk 和向量，但不会删除磁盘上的原始文件。更换 Embedding 后需要重新导入文档。",
                    "清空知识库",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
            {
                return;
            }

            await _store.ClearKnowledgeBaseAsync(_disposeCts.Token);
            SelectedDocument = null;
            await RefreshDocumentsAsync();
            Status = "知识库已清空，请用当前 Embedding 模型重新导入文档";
            AddLog("Info", Status);
        }
        catch (Exception ex)
        {
            _avatar.Set(AvatarState.Error);
            Status = ex.Message;
            AddLog("Error", $"Clear knowledge base failed: {ex.Message}");
        }
    }

    private bool CanDeleteSelectedDocument() => SelectedDocument is not null;

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync()
    {
        var userQuestion = Question.Trim();
        if (string.IsNullOrWhiteSpace(userQuestion))
        {
            return;
        }

        Question = "";
        Conversation.Add(CreateConversationItem("user", userQuestion));
        var thinkingItem = CreateConversationItem("assistant", "正在思考...");
        Conversation.Add(thinkingItem);
        try
        {
            _avatar.Set(AvatarState.Thinking);
            Status = ThinkingStatusText;
            LogRuntimeSnapshot("Ask");
            var answer = await _rag.AskAsync(userQuestion, RetrievalTopK, _disposeCts.Token);
            var answerItem = CreateConversationItem(
                "assistant",
                answer.Answer,
                string.Join(Environment.NewLine, answer.Citations.Select((citation, index) =>
                    $"[{index + 1}] {citation.Chunk.SourceLabel} score={citation.Score:0.000}")));
            ReplaceConversationItem(thinkingItem, answerItem);
            StartAssistantSpeech(answer.Answer);
        }
        catch (Exception ex)
        {
            _avatar.Set(AvatarState.Error);
            AddExceptionLog("Ask failed", ex);
            ReplaceConversationItem(thinkingItem, CreateConversationItem("assistant", $"出错：{ex.Message}"));
            Status = ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanVoiceAsk))]
    private async Task VoiceAskAsync()
    {
        if (IsVoiceAskBusy)
        {
            return;
        }

        IsVoiceAskBusy = true;
        try
        {
            _avatar.Set(AvatarState.Listening);
            Status = "正在聆听";
            VoiceInteractionHint = "正在聆听，请直接说出完整问题；录音约 6 秒后会自动识别。";
            LogRuntimeSnapshot("VoiceAsk");
            var recognized = await _speech.RecognizeOnceAsync(_disposeCts.Token);
            if (string.IsNullOrWhiteSpace(recognized))
            {
                Status = "未识别到语音";
                VoiceInteractionHint = "未识别到语音，请靠近麦克风后再试。";
                _avatar.Set(AvatarState.Idle);
                return;
            }

            Status = $"已识别：{recognized}";
            VoiceInteractionHint = "已收到语音，正在检索知识库并生成回答。";
            Question = recognized;
            await AskAsync();
            VoiceInteractionHint = "回答完成，可以继续点击语音提问。";
        }
        catch (Exception ex)
        {
            _avatar.Set(AvatarState.Error);
            AddExceptionLog("Voice ask failed", ex);
            Status = ex.Message;
            VoiceInteractionHint = $"语音识别失败：{ex.Message}";
        }
        finally
        {
            IsVoiceAskBusy = false;
            if (Status == "正在聆听")
            {
                Status = IdleStatusText;
            }
        }
    }

    private bool CanVoiceAsk() => !IsVoiceAskBusy;

    [RelayCommand]
    private async Task PlayGreetingAsync()
    {
        try
        {
            _avatar.Set(AvatarState.Speaking);
            Status = SpeakingStatusText;
            LogRuntimeSnapshot("Greeting TTS");
            await _speech.SpeakAsync(GreetingText, _disposeCts.Token);
            _avatar.Set(AvatarState.Idle);
            Status = IdleStatusText;
        }
        catch (Exception ex)
        {
            _avatar.Set(AvatarState.Idle);
            AddExceptionLog("Greeting failed", ex);
            Status = $"语音输出失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task AskQuickQuestionAsync(QuickQuestionItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Text))
        {
            return;
        }

        Question = item.Text;
        await AskAsync();
    }

    private void StartAssistantSpeech(string answer)
    {
        _ = SpeakAssistantAnswerAsync(answer);
    }

    private async Task SpeakAssistantAnswerAsync(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            Status = IdleStatusText;
            _avatar.Set(AvatarState.Idle);
            return;
        }

        try
        {
            _avatar.Set(AvatarState.Speaking);
            Status = SpeakingStatusText;
            LogRuntimeSnapshot("Answer TTS");
            await _speech.SpeakAsync(answer, _disposeCts.Token);
            Status = IdleStatusText;
            _avatar.Set(AvatarState.Idle);
        }
        catch (Exception ex)
        {
            AddExceptionLog("TTS failed", ex);
            Status = $"语音输出失败：{ex.Message}";
            _avatar.Set(AvatarState.Idle);
        }
    }

    [RelayCommand]
    private async Task ClearVisitorSessionAsync()
    {
        Conversation.Clear();
        Question = "";
        await _store.ClearMessagesAsync(_disposeCts.Token);
        Status = IdleStatusText;
        _avatar.Set(AvatarState.Idle);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        ApplyLlmProviderFormToSettings(saveApiKey: true);
        _settings.ChatProviderId = "openai-chat";
        SelectedChatProviderId = "openai-chat";
        _settings.EmbeddingProviderId = SelectedEmbeddingProviderId;
        _settings.SpeechRecognizerId = SelectedSpeechRecognizerId;
        _settings.SpeechSynthesizerId = SelectedSpeechSynthesizerId;
        ApplyRuntimeCapabilityProviderSettings();
        if (!ApplyVisitorInteractionSettings())
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(ApiKeyToSave))
        {
            _credentialService.WriteSecret(OpenAiCompatibleChatProvider.CredentialName("openai-compatible"), ApiKeyToSave.Trim());
            ApiKeyToSave = "";
        }

        await _settingsService.SaveAsync(_settings, _disposeCts.Token);
        if (IsMaskedOrFilledApiKey(LlmApiKeyToSave))
        {
            LlmApiKeyToSave = MaskedApiKey;
        }

        RefreshQuickQuestions();
        RefreshAssistantTags();
        SettingsApplied?.Invoke(this, _settings);
        AddLog("Info", "Settings saved");
        Status = "设置已保存";
    }

    [RelayCommand]
    private async Task SaveVisitorInteractionSettingsAsync()
    {
        try
        {
            if (!ApplyVisitorInteractionSettings())
            {
                return;
            }

            await _settingsService.SaveAsync(_settings, _disposeCts.Token);
            RefreshQuickQuestions();
            RefreshAssistantTags();
            SettingsApplied?.Invoke(this, _settings);
            Status = "游客交互设置已保存";
            AddLog("Info", Status);
        }
        catch (Exception ex)
        {
            Status = $"保存游客交互设置失败：{ex.Message}";
            AddLog("Error", Status);
        }
    }

    private bool ApplyVisitorInteractionSettings()
    {
        _settings.AssistantName = Required(AssistantName, AppSettings.DefaultAssistantName);
        _settings.VisitorWindowTitle = Required(VisitorWindowTitle, _settings.AssistantName);
        _settings.VisitorHeadline = Required(VisitorHeadline, $"您好，我是{_settings.AssistantName}");
        _settings.GreetingText = GreetingText.Trim();
        _settings.WakeHintText = Required(WakeHintText, "可点击桌宠，或说“助手”唤醒。");
        _settings.PetHintText = Required(PetHintText, "点我咨询");
        _settings.AdminButtonText = Required(AdminButtonText, "管理员");
        _settings.CloseButtonText = Required(CloseButtonText, "关闭");
        _settings.EndSessionButtonText = Required(EndSessionButtonText, "结束本次咨询");
        _settings.QuickQuestionsHeader = Required(QuickQuestionsHeader, "常见问题");
        _settings.ConversationHeader = Required(ConversationHeader, "问答记录");
        _settings.SendButtonText = Required(SendButtonText, "发送");
        _settings.VoiceButtonText = Required(VoiceButtonText, "语音提问");
        _settings.IdleStatusText = Required(IdleStatusText, "待命");
        _settings.ListeningStatusText = Required(ListeningStatusText, "聆听");
        _settings.ThinkingStatusText = Required(ThinkingStatusText, "思考");
        _settings.SpeakingStatusText = Required(SpeakingStatusText, "说话");
        _settings.ErrorStatusText = Required(ErrorStatusText, "异常");
        _settings.PetImagePath = PetImagePath.Trim();
        _settings.PetFramesDirectory = PetFramesDirectory.Trim();
        _settings.PetFrameIntervalMs = Math.Max(120, PetFrameIntervalMs);
        _settings.LogoImagePath = Required(LogoImagePath, AppSettings.DefaultLogoImagePath);
        PetPreviewImagePath = ResolvePetPreviewImagePath(_settings);
        LogoPreviewImagePath = ResolveLogoPreviewImagePath(_settings);
        _settings.WakeWords = WakeWordsText
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        if (_settings.WakeWords.Count == 0)
        {
            _settings.WakeWords.Add("助手");
        }

        _settings.AssistantTags = AssistantTagsText
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(6)
            .ToList();
        if (_settings.AssistantTags.Count == 0)
        {
            _settings.AssistantTags.AddRange(["知识库问答", "语音交互", "触屏友好", "本地索引"]);
        }

        _settings.QuickQuestions = QuickQuestionsText
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(8)
            .ToList();
        _settings.RetrievalTopK = Math.Max(1, RetrievalTopK);
        _settings.SystemPrompt = Required(SystemPromptText, AppSettings.DefaultSystemPrompt);
        _settings.CharacterPrompt = Required(CharacterPromptText, AppSettings.DefaultCharacterPrompt);
        _settings.WorldBookEntries = ParseWorldBook(WorldBookText);
        if (!string.IsNullOrWhiteSpace(NewAdminPin))
        {
            if (NewAdminPin.Length < 6)
            {
                Status = "管理员 PIN 至少 6 位";
                return false;
            }

            _settings.AdminPinHash = HashPin(NewAdminPin);
            NewAdminPin = "";
        }

        return true;
    }

    [RelayCommand]
    private async Task RefreshLlmModelsAsync()
    {
        try
        {
            LlmModelStatus = "正在获取模型列表...";
            var provider = _llmProviderCatalog.GetProvider(LlmSelectedProviderCode);
            var chatConfig = _providers.GetConfig("openai-chat");
            var effectiveBaseUrl = LlmProviderCatalogService.ResolveBaseUrlForProvider(provider.Code, LlmBaseUrl);
            LlmBaseUrl = effectiveBaseUrl;
            var options = new Dictionary<string, string>(provider.DefaultOptions, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in chatConfig.Options)
            {
                options[key] = value;
            }

            var models = await _llmProviderCatalog.FetchModelsAsync(
                provider.Code,
                effectiveBaseUrl,
                ReadableApiKeyInput(LlmApiKeyToSave),
                options,
                _disposeCts.Token);
            RefreshLlmModelOptions(provider, chatConfig, models);
            RefreshRuntimeCapabilityModelOptions(models);
            LlmSelectedModel = models.FirstOrDefault() ?? LlmSelectedModel;
            LlmProviderCatalogService.ApplyProviderToChatConfig(
                chatConfig,
                provider.Code,
                Required(effectiveBaseUrl, provider.DefaultBaseUrl),
                Required(LlmSelectedModel, provider.RecommendModels.FirstOrDefault() ?? ""),
                models);
            chatConfig.Options["enableThinking"] = LlmEnableThinking ? "true" : "false";
            await _settingsService.SaveAsync(_settings, _disposeCts.Token);
            LlmModelStatus = models.Count == 0 ? "供应商未返回模型列表，可手动填写模型名" : $"已获取 {models.Count} 个模型，已同步到 Embedding/ASR/TTS 下拉";
        }
        catch (Exception ex)
        {
            LlmModelStatus = $"获取模型失败：{ex.Message}";
            AddLog("Error", LlmModelStatus);
        }
    }

    [RelayCommand]
    private async Task SaveLlmProviderAsync()
    {
        try
        {
            ApplyLlmProviderFormToSettings(saveApiKey: true);
            ApplyRuntimeCapabilityProviderSettings();
            await _settingsService.SaveAsync(_settings, _disposeCts.Token);
            SelectedChatProviderId = "openai-chat";
            SettingsApplied?.Invoke(this, _settings);
            LlmApiKeyToSave = MaskedApiKey;
            LlmModelStatus = "LLM 供应商配置已保存，API Key 已加密保存";
            AddLog("Info", LlmModelStatus);
        }
        catch (Exception ex)
        {
            LlmModelStatus = $"保存 LLM 供应商失败：{ex.Message}";
            AddLog("Error", LlmModelStatus);
        }
    }

    [RelayCommand]
    private async Task SaveRuntimeCapabilitySettingsAsync()
    {
        try
        {
            _settings.EmbeddingProviderId = SelectedEmbeddingProviderId;
            _settings.SpeechRecognizerId = SelectedSpeechRecognizerId;
            _settings.SpeechSynthesizerId = SelectedSpeechSynthesizerId;
            ApplyRuntimeCapabilityProviderSettings();
            LoadRuntimeCapabilityModels();
            await _settingsService.SaveAsync(_settings, _disposeCts.Token);
            SettingsApplied?.Invoke(this, _settings);
            Status = "检索与语音设置已保存";
            AddLog("Info", Status);
        }
        catch (Exception ex)
        {
            Status = $"保存检索与语音设置失败：{ex.Message}";
            AddLog("Error", Status);
        }
    }

    [RelayCommand]
    private async Task TestTtsAsync()
    {
        try
        {
            _settings.EmbeddingProviderId = SelectedEmbeddingProviderId;
            _settings.SpeechRecognizerId = SelectedSpeechRecognizerId;
            _settings.SpeechSynthesizerId = SelectedSpeechSynthesizerId;
            ApplyRuntimeCapabilityProviderSettings();
            await _settingsService.SaveAsync(_settings, _disposeCts.Token);
            SettingsApplied?.Invoke(this, _settings);

            var text = Required(TtsPreviewText, "您好，请问有什么需要了解的？");
            Status = "正在试听语音输出";
            _avatar.Set(AvatarState.Speaking);
            LogRuntimeSnapshot("TTS preview");
            await _speech.SpeakAsync(text, _disposeCts.Token);
            Status = "语音试听完成";
            _avatar.Set(AvatarState.Idle);
            AddLog("Info", Status);
        }
        catch (Exception ex)
        {
            Status = $"语音试听失败：{ex.Message}";
            _avatar.Set(AvatarState.Error);
            AddExceptionLog("TTS preview failed", ex);
        }
    }

    [RelayCommand]
    private async Task TestAsrAudioAsync()
    {
        try
        {
            _settings.EmbeddingProviderId = SelectedEmbeddingProviderId;
            _settings.SpeechRecognizerId = SelectedSpeechRecognizerId;
            _settings.SpeechSynthesizerId = SelectedSpeechSynthesizerId;
            ApplyRuntimeCapabilityProviderSettings();
            await _settingsService.SaveAsync(_settings, _disposeCts.Token);
            SettingsApplied?.Invoke(this, _settings);

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择用于 ASR 测试的音频",
                Filter = "音频文件|*.wav;*.mp3;*.m4a;*.mp4;*.webm;*.ogg;*.flac|所有文件|*.*",
                InitialDirectory = GetAsrTestInitialDirectory()
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            AsrTestAudioPath = dialog.FileName;
            Status = "正在测试语音识别";
            _avatar.Set(AvatarState.Listening);
            LogRuntimeSnapshot("ASR file test");
            var text = await _speech.TranscribeFileAsync(AsrTestAudioPath, _disposeCts.Token);
            AsrTestResult = text;
            Status = $"ASR 测试结果：{text}";
            _avatar.Set(AvatarState.Idle);
            AddLog("Info", $"ASR 测试完成\nFile: {AsrTestAudioPath}\nText: {text}");
        }
        catch (Exception ex)
        {
            Status = $"ASR 测试失败：{ex.Message}";
            _avatar.Set(AvatarState.Error);
            AddExceptionLog("ASR file test failed", ex);
        }
    }

    public bool ValidateAdminPin(string pin) =>
        !string.IsNullOrWhiteSpace(pin) &&
        HashPin(pin).Equals(_settings.AdminPinHash, StringComparison.OrdinalIgnoreCase);

    private bool CanAsk() => !IsVoiceAskBusy && !string.IsNullOrWhiteSpace(Question);

    partial void OnQuestionChanged(string value) => AskCommand.NotifyCanExecuteChanged();

    partial void OnIsVoiceAskBusyChanged(bool value) => OnPropertyChanged(nameof(VoiceAskButtonDisplayText));

    partial void OnVoiceButtonTextChanged(string value) => OnPropertyChanged(nameof(VoiceAskButtonDisplayText));

    private async Task InitializeAsync()
    {
        await RefreshDocumentsAsync();
        var messages = await _store.GetRecentMessagesAsync(30, _disposeCts.Token);
        foreach (var message in messages)
        {
            Conversation.Add(CreateConversationItem(message.Role, message.Content));
        }
    }

    private ConversationItem CreateConversationItem(string role, string content, string citationText = "") =>
        new()
        {
            Role = role,
            Content = content,
            CitationText = citationText,
            AssistantAvatarPath = role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? ResolvePetPreviewImagePath(_settings)
                : ""
        };

    private void ReplaceConversationItem(ConversationItem oldItem, ConversationItem newItem)
    {
        var index = Conversation.IndexOf(oldItem);
        if (index >= 0)
        {
            Conversation[index] = newItem;
        }
        else
        {
            Conversation.Add(newItem);
        }
    }

    private async Task RefreshDocumentsAsync()
    {
        Documents.Clear();
        foreach (var document in await _store.GetDocumentsAsync(_disposeCts.Token))
        {
            Documents.Add(new DocumentItem
            {
                Id = document.Id,
                Title = document.Title,
                Path = document.Path,
                CreatedAt = document.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            });
        }
    }

    private void FillProviderOptions()
    {
        Add(EmbeddingProviders, "local-hash-embedding");
        Add(EmbeddingProviders, "openai-embedding");
        Add(SpeechRecognizerProviders, "windows-dictation");
        Add(SpeechRecognizerProviders, "whisper-local");
        Add(SpeechRecognizerProviders, "openai-asr");
        Add(SpeechSynthesizerProviders, "local-vits-tts");
        Add(SpeechSynthesizerProviders, "aliyun-qwen-tts");
        Add(SpeechSynthesizerProviders, "local-command-tts");
        Add(SpeechSynthesizerProviders, "windows-tts");
        Add(SpeechSynthesizerProviders, "openai-tts");

        void Add(ObservableCollection<ProviderOption> target, string providerId)
        {
            var config = _providers.GetConfig(providerId);
            target.Add(new ProviderOption(config.ProviderId, config.DisplayName));
        }
    }

    private void FillLlmProviderOptions()
    {
        LlmProviderOptions.Clear();
        foreach (var provider in _llmProviderCatalog.Providers)
        {
            LlmProviderOptions.Add(new LlmProviderOptionView(provider.Code, provider.Name));
        }
    }

    private void LoadLlmProviderForm()
    {
        var chatConfig = _providers.GetConfig("openai-chat");
        var providerCode = chatConfig.Options.GetValueOrDefault("llmProviderCode", "aliyun");
        var provider = _llmProviderCatalog.GetProvider(providerCode);
        _loadingLlmProviderForm = true;
        try
        {
            LlmSelectedProviderCode = provider.Code;
            var configuredBaseUrl = chatConfig.Options.GetValueOrDefault("baseUrl", ExtractBaseUrl(chatConfig.Endpoint, provider.DefaultBaseUrl));
            LlmBaseUrl = LlmProviderCatalogService.ResolveBaseUrlForProvider(provider.Code, configuredBaseUrl);
            LlmSelectedModel = string.IsNullOrWhiteSpace(chatConfig.Model)
                ? provider.RecommendModels.FirstOrDefault() ?? ""
                : chatConfig.Model;
            LlmEnableThinking = bool.TryParse(chatConfig.Options.GetValueOrDefault("enableThinking", "false"), out var enableThinking) && enableThinking;
            LlmApiKeyToSave = _credentialService.ReadSecret(LlmProviderCatalogService.CredentialName(provider.Code)) is null
                ? ""
                : MaskedApiKey;

            RefreshLlmModelOptions(provider, chatConfig, []);
            LlmModelStatus = "可选择推荐模型，或填写 API Key 后获取供应商模型列表。";
        }
        finally
        {
            _loadingLlmProviderForm = false;
        }
    }

    private void RefreshLlmModelOptions(
        LlmProviderDefinition provider,
        ProviderConfig chatConfig,
        IReadOnlyList<string> fetchedModels)
    {
        LlmModelOptions.Clear();
        var models = fetchedModels.Count > 0
            ? fetchedModels
            : LlmProviderCatalogService.GetCachedModels(chatConfig, provider);
        foreach (var model in models.Where(model => !string.IsNullOrWhiteSpace(model)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            LlmModelOptions.Add(model);
        }

        if (!string.IsNullOrWhiteSpace(LlmSelectedModel) && !LlmModelOptions.Contains(LlmSelectedModel))
        {
            LlmModelOptions.Insert(0, LlmSelectedModel);
        }
    }

    private void LoadRuntimeCapabilityModels()
    {
        if (string.IsNullOrWhiteSpace(SelectedEmbeddingProviderId) ||
            string.IsNullOrWhiteSpace(SelectedSpeechRecognizerId) ||
            string.IsNullOrWhiteSpace(SelectedSpeechSynthesizerId))
        {
            return;
        }

        var embeddingConfig = _providers.GetConfig(SelectedEmbeddingProviderId);
        var asrConfig = _providers.GetConfig(SelectedSpeechRecognizerId);
        var ttsConfig = _providers.GetConfig(SelectedSpeechSynthesizerId);
        var nextEmbeddingModel = Required(embeddingConfig.Model, SelectedEmbeddingProviderId == "openai-embedding" ? "text-embedding-v4" : "local-hash-384");
        var nextAsrModel = Required(asrConfig.Model, SelectedSpeechRecognizerId == "openai-asr" ? "qwen3-asr-flash-realtime" : "system-speech");
        var nextTtsModel = NormalizeSelectedTtsModel(Required(ttsConfig.Model, GetDefaultTtsModel()));
        EmbeddingModel = nextEmbeddingModel;
        AsrModel = nextAsrModel;
        TtsModel = nextTtsModel;
        TtsEndpoint = ttsConfig.Endpoint;
        TtsWorkspaceId = Required(
            ttsConfig.Options.GetValueOrDefault("workspaceId", ""),
            ExtractAliyunWorkspaceId(ttsConfig.Options.GetValueOrDefault("realtimeEndpoint", ttsConfig.Endpoint)));
        TtsVoice = NormalizeTtsVoice(Required(ttsConfig.Options.GetValueOrDefault("voice", ""), GetDefaultTtsVoice()));
        if (SelectedSpeechSynthesizerId.Equals("local-vits-tts", StringComparison.OrdinalIgnoreCase))
        {
            TtsVoice = NormalizeLocalVitsVoiceForUi(nextTtsModel, TtsVoice);
        }
        TtsSpeed = ParseTtsSpeed(ttsConfig.Options.GetValueOrDefault("speed", ""), 1.0);
        TtsInstructions = ttsConfig.Options.GetValueOrDefault("instructions", "");
        TtsOptimizeInstructions = bool.TryParse(ttsConfig.Options.GetValueOrDefault("optimize_instructions", "false"), out var optimize) && optimize;
        TtsConfigHint = GetTtsConfigHint();
        var chatConfig = _providers.GetConfig("openai-chat");
        string[] cachedModels = chatConfig.Options.TryGetValue("dynamicModels", out var dynamicModels)
            ? dynamicModels.Split(["\n", "\r\n", ",", "，"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        RefreshRuntimeCapabilityModelOptions(cachedModels);
        EmbeddingModel = nextEmbeddingModel;
        AsrModel = nextAsrModel;
        TtsModel = nextTtsModel;
    }

    private void RefreshRuntimeCapabilityModelOptions(IReadOnlyList<string> fetchedModels)
    {
        FillModelOptions(
            EmbeddingModelOptions,
            SelectedEmbeddingProviderId == "openai-embedding" ? FilterModels(fetchedModels, ["embedding", "embed", "text-embedding"]) : [],
            EmbeddingModel,
            SelectedEmbeddingProviderId == "openai-embedding"
                ? ["text-embedding-v4", "text-embedding-v3", "text-embedding-3-small", "text-embedding-3-large"]
                : ["local-hash-384"]);
        FillModelOptions(
            AsrModelOptions,
            SelectedSpeechRecognizerId == "openai-asr" ? FilterModels(fetchedModels, ["asr", "transcribe", "whisper"]) : [],
            AsrModel,
            SelectedSpeechRecognizerId == "openai-asr"
                ? ["qwen3-asr-flash-realtime", "qwen3-asr-flash-realtime-2026-02-10", "qwen3-asr-flash-filetrans", "whisper-1", "gpt-4o-transcribe", "gpt-4o-mini-transcribe"]
                : SelectedSpeechRecognizerId == "whisper-local" ? ["ggml-base.bin"] : ["system-speech"]);
        FillModelOptions(
            TtsModelOptions,
            FilterTtsModelsForSelectedProvider(fetchedModels),
            TtsModel,
            GetTtsFallbackModels());
    }

    private IReadOnlyList<string> GetTtsFallbackModels() =>
        SelectedSpeechSynthesizerId switch
        {
            "aliyun-qwen-tts" => ["qwen3-tts-instruct-flash", "qwen3-tts-flash", "qwen3-tts-instruct-flash-realtime", "qwen3-tts-flash-realtime"],
            "openai-tts" => ["gpt-4o-mini-tts", "tts-1", "tts-1-hd"],
            "local-vits-tts" => DiscoverLocalModels("Tools\\VITS", "*.onnx", SearchOption.AllDirectories),
            "local-command-tts" => DiscoverKokoroModels(),
            _ => ["system-speech"]
        };

    private string GetDefaultTtsModel() =>
        SelectedSpeechSynthesizerId switch
        {
            "aliyun-qwen-tts" => "qwen3-tts-instruct-flash",
            "openai-tts" => "gpt-4o-mini-tts",
            "local-vits-tts" => "Tools\\VITS\\vits-melo-tts-zh_en\\model.onnx",
            "local-command-tts" => "Tools\\Kokoro\\kokoro-multi-lang-v1_1\\model.onnx",
            _ => "system-speech"
        };

    private string NormalizeSelectedTtsModel(string model) =>
        SelectedSpeechSynthesizerId switch
        {
            "local-command-tts" => NormalizeKokoroTtsModel(model),
            "local-vits-tts" => NormalizeLocalVitsTtsModel(model),
            _ => model
        };

    private string NormalizeLocalVitsTtsModel(string model)
    {
        var value = model.Trim();
        var modelPath = Path.IsPathRooted(value)
            ? value
            : Path.Combine(AppContext.BaseDirectory, value);
        return string.IsNullOrWhiteSpace(value) ||
            value.Contains("vits-zh-hf-theresa", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("vits-zh-hf-eula", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(modelPath)
            ? GetDefaultTtsModel()
            : value;
    }

    private static string NormalizeKokoroTtsModel(string model)
    {
        var value = model.Trim();
        return value.Contains("kokoro-int8", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("kokoro-multi-lang-v1_0", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("model.int8.onnx", StringComparison.OrdinalIgnoreCase)
            ? "Tools\\Kokoro\\kokoro-multi-lang-v1_1\\model.onnx"
            : value;
    }

    private string GetDefaultTtsVoice() =>
        SelectedSpeechSynthesizerId switch
        {
            "aliyun-qwen-tts" => "Cherry",
            "openai-tts" => "alloy",
            "local-vits-tts" => "0",
            "local-command-tts" => "3",
            _ => ""
        };

    private string NormalizeTtsVoice(string voice) =>
        SelectedSpeechSynthesizerId == "aliyun-qwen-tts"
            ? NormalizeAliyunTtsVoice(voice)
            : voice;

    private static double ParseTtsSpeed(string value, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
            ? ClampTtsSpeed(speed)
            : fallback;
    }

    private static double ClampTtsSpeed(double speed) => Math.Clamp(speed, 0.5, 2.0);

    private static string FormatTtsSpeed(double speed) =>
        ClampTtsSpeed(speed).ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatTtsLengthScale(double speed) =>
        (1.0 / ClampTtsSpeed(speed)).ToString("0.###", CultureInfo.InvariantCulture);

    private static string BuildLocalVitsArguments(string model, string lengthScale)
    {
        var ruleFstsArgument = LocalVitsModelHasRuleFsts(model)
            ? " --tts-rule-fsts=\"{modelDir}\\phone.fst,{modelDir}\\number.fst\""
            : "";
        return $"--debug=0 --vits-model=\"{{model}}\" --vits-lexicon=\"{{modelDir}}\\lexicon.txt\" --vits-tokens=\"{{modelDir}}\\tokens.txt\"{ruleFstsArgument} --vits-length-scale={lengthScale} --num-threads=4 --sid={{voice}} --output-filename=\"{{output}}\" \"{{text}}\"";
    }

    private static string GetLocalVitsSinglePassMaxChars(string model) =>
        model.Contains("sherpa-onnx-vits-zh-ll", StringComparison.OrdinalIgnoreCase) ? "70" : "140";

    private static bool LocalVitsModelHasRuleFsts(string model)
    {
        var modelPath = Path.IsPathRooted(model)
            ? model
            : Path.Combine(AppContext.BaseDirectory, model);
        var modelDirectory = Path.GetDirectoryName(modelPath);
        return !string.IsNullOrWhiteSpace(modelDirectory) &&
            File.Exists(Path.Combine(modelDirectory, "phone.fst")) &&
            File.Exists(Path.Combine(modelDirectory, "number.fst"));
    }

    private static string GetDefaultLocalVitsVoice(string model) =>
        model.Contains("fanchen-C", StringComparison.OrdinalIgnoreCase) ? "14" : "0";

    private static string NormalizeLocalVitsVoiceForUi(string model, string voice)
    {
        var defaultVoice = GetDefaultLocalVitsVoice(model);
        var maxSid = model.Contains("fanchen-C", StringComparison.OrdinalIgnoreCase)
            ? 186
            : model.Contains("sherpa-onnx-vits-zh-ll", StringComparison.OrdinalIgnoreCase)
                ? 4
                : model.Contains("vits-melo-tts-zh_en", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("breeze2-vits-onnx", StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : int.MaxValue;

        return int.TryParse(voice, out var sid) && sid >= 0 && sid <= maxSid
            ? sid.ToString(CultureInfo.InvariantCulture)
            : defaultVoice;
    }

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

    private IReadOnlyList<string> GetTtsModelKeywords() =>
        SelectedSpeechSynthesizerId switch
        {
            "aliyun-qwen-tts" => ["tts", "qwen3-tts"],
            "openai-tts" => ["gpt-4o-mini-tts", "tts-1"],
            _ => []
        };

    private string GetTtsConfigHint() =>
        SelectedSpeechSynthesizerId switch
        {
            "local-vits-tts" => "本地 VITS 中文动漫音色：路径/Endpoint = sherpa-onnx-offline-tts.exe；模型 = Tools\\VITS 下的 .onnx；Voice 填 speaker id。melo、breeze2 为单女声填 0；zh-ll 可试 0-4；fanchen-C 可试 0、14、32、68、100、102。语速越大越快，建议 1.0-1.4。",
            "local-command-tts" => "Kokoro ONNX：路径/Endpoint = Tools\\Kokoro\\bin\\sherpa-onnx-offline-tts.exe；模型 = Tools\\Kokoro\\kokoro-multi-lang-v1_1\\model.onnx；v1.1 有 103 个音色，中文女声可试 3-57，中文男声可试 58-102。",
            "aliyun-qwen-tts" => "阿里百炼 Qwen TTS：普通模型走 HTTP，不需要手写 Endpoint；Realtime 模型需要填写业务空间 ID。Voice 可填 Cherry 等百炼支持的音色。",
            "openai-tts" => "OpenAI-compatible TTS 使用当前 LLM 供应商的 /v1/audio/speech；Voice 填供应商支持的音色。",
            "windows-tts" => "Windows TTS 使用系统语音，不需要模型、路径或 API Key。",
            _ => "当前语音输出不需要额外配置。"
        };

    private IReadOnlyList<string> FilterTtsModelsForSelectedProvider(IReadOnlyList<string> models)
    {
        if (SelectedSpeechSynthesizerId == "aliyun-qwen-tts")
        {
            return FilterModels(models, ["qwen3-tts"]);
        }

        if (SelectedSpeechSynthesizerId == "openai-tts")
        {
            return models
                .Where(model =>
                    !model.Contains("qwen3-tts", StringComparison.OrdinalIgnoreCase) &&
                    !model.Contains("asr", StringComparison.OrdinalIgnoreCase) &&
                    !model.Contains("embedding", StringComparison.OrdinalIgnoreCase) &&
                    (model.Equals("tts-1", StringComparison.OrdinalIgnoreCase) ||
                        model.Equals("tts-1-hd", StringComparison.OrdinalIgnoreCase) ||
                        model.Contains("gpt-4o", StringComparison.OrdinalIgnoreCase) &&
                        model.Contains("tts", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        return [];
    }

    private static IReadOnlyList<string> DiscoverLocalModels(
        string relativeDirectory,
        string searchPattern,
        SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, searchPattern, searchOption)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(AppContext.BaseDirectory, path))
            .ToArray();
    }

    private static IReadOnlyList<string> DiscoverKokoroModels()
    {
        var officialModel = Path.Combine(AppContext.BaseDirectory, "Tools", "Kokoro", "kokoro-multi-lang-v1_1", "model.onnx");
        if (File.Exists(officialModel))
        {
            return [Path.GetRelativePath(AppContext.BaseDirectory, officialModel)];
        }

        return DiscoverLocalModels("Tools\\Kokoro", "model.onnx", SearchOption.AllDirectories);
    }

    private static IReadOnlyList<string> FilterModels(IReadOnlyList<string> models, IReadOnlyList<string> keywords)
    {
        if (keywords.Count == 0)
        {
            return [];
        }

        return models
            .Where(model => keywords.Any(keyword => model.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static void FillModelOptions(
        ObservableCollection<string> target,
        IReadOnlyList<string> fetchedModels,
        string selectedModel,
        IReadOnlyList<string> fallbackModels)
    {
        var orderedModels = fetchedModels
            .Concat(fallbackModels)
            .Prepend(selectedModel)
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var model in orderedModels)
        {
            if (!target.Contains(model))
            {
                target.Add(model);
            }
        }

        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!orderedModels.Contains(target[i], StringComparer.OrdinalIgnoreCase))
            {
                target.RemoveAt(i);
            }
        }
    }

    private void ApplyRuntimeCapabilityProviderSettings()
    {
        var provider = _llmProviderCatalog.GetProvider(LlmSelectedProviderCode);
        var baseUrl = LlmProviderCatalogService.ResolveBaseUrlForProvider(provider.Code, LlmBaseUrl);
        var authRef = $"llm-{provider.Code}";

        ApplyCompatibleRuntimeConfig("openai-embedding", $"{provider.Name} Embedding", baseUrl, "/v1/embeddings", Required(EmbeddingModel, "text-embedding-3-small"), authRef);
        ApplyAsrRuntimeConfig(provider.Name, provider.Code, baseUrl, authRef);
        if (SelectedSpeechSynthesizerId == "openai-tts")
        {
            ApplyCompatibleRuntimeConfig("openai-tts", $"{provider.Name} TTS", baseUrl, "/v1/audio/speech", Required(TtsModel, "gpt-4o-mini-tts"), authRef);
            _providers.GetConfig("openai-tts").Options["voice"] = Required(TtsVoice, "alloy");
        }
        else if (SelectedSpeechSynthesizerId == "aliyun-qwen-tts")
        {
            var config = _providers.GetConfig("aliyun-qwen-tts");
            config.DisplayName = "阿里百炼 Qwen TTS";
            var model = Required(TtsModel, "qwen3-tts-instruct-flash");
            var endpoint = BuildAliyunQwenTtsEndpoint(model, TtsWorkspaceId);

            config.Endpoint = endpoint;
            config.Model = model;
            config.AuthRef = "llm-aliyun";
            config.Options["voice"] = NormalizeAliyunTtsVoice(TtsVoice);
            config.Options["language_type"] = "Chinese";
            config.Options["instructions"] = TtsInstructions.Trim();
            config.Options["optimize_instructions"] = TtsOptimizeInstructions ? "true" : "false";
            config.Options["workspaceId"] = TtsWorkspaceId.Trim();
            config.Options["realtimeEndpoint"] = endpoint;
        }
        else if (SelectedSpeechSynthesizerId == "local-vits-tts")
        {
            var config = _providers.GetConfig("local-vits-tts");
            var speed = FormatTtsSpeed(TtsSpeed);
            var lengthScale = FormatTtsLengthScale(TtsSpeed);
            config.DisplayName = "本地 VITS 中文动漫音色 (sherpa-onnx)";
            config.Endpoint = Required(TtsEndpoint, "Tools\\VITS\\bin\\sherpa-onnx-offline-tts.exe");
            config.Model = Required(TtsModel, "Tools\\VITS\\vits-melo-tts-zh_en\\model.onnx");
            config.AuthRef = "";
            config.Options["voice"] = NormalizeLocalVitsVoiceForUi(config.Model, Required(TtsVoice, GetDefaultLocalVitsVoice(config.Model)));
            config.Options["speed"] = speed;
            config.Options["inputMode"] = "argument";
            config.Options["singlePassMaxChars"] = GetLocalVitsSinglePassMaxChars(config.Model);
            config.Options["arguments"] = BuildLocalVitsArguments(config.Model, lengthScale);
        }
        else if (SelectedSpeechSynthesizerId == "local-command-tts")
        {
            var config = _providers.GetConfig("local-command-tts");
            var speed = FormatTtsSpeed(TtsSpeed);
            var lengthScale = FormatTtsLengthScale(TtsSpeed);
            var isKokoro = TtsEndpoint.Contains("Kokoro", StringComparison.OrdinalIgnoreCase) ||
                TtsModel.Contains("kokoro", StringComparison.OrdinalIgnoreCase);
            var kokoroModel = NormalizeKokoroTtsModel(Required(TtsModel, "Tools\\Kokoro\\kokoro-multi-lang-v1_1\\model.onnx"));
            config.DisplayName = isKokoro
                ? $"Kokoro Local ONNX TTS (sherpa, sid {Required(TtsVoice, "3")})"
                : "External Local TTS Command";
            config.Endpoint = isKokoro
                ? Required(TtsEndpoint, "Tools\\Kokoro\\bin\\sherpa-onnx-offline-tts.exe")
                : TtsEndpoint.Trim();
            config.Model = isKokoro
                ? kokoroModel
                : TtsModel.Trim();
            config.AuthRef = "";
            config.Options["voice"] = TtsVoice.Trim();
            config.Options["speed"] = speed;
            if (isKokoro)
            {
                config.Options["inputMode"] = "argument";
                config.Options["singlePassMaxChars"] = "220";
                config.Options["arguments"] = $"--debug=0 --kokoro-model=\"{{model}}\" --kokoro-voices=\"{{modelDir}}\\voices.bin\" --kokoro-tokens=\"{{modelDir}}\\tokens.txt\" --kokoro-data-dir=\"{{modelDir}}\\espeak-ng-data\" --kokoro-lexicon=\"{{modelDir}}\\lexicon-us-en.txt,{{modelDir}}\\lexicon-zh.txt\" --tts-rule-fsts=\"{{modelDir}}\\date-zh.fst,{{modelDir}}\\phone-zh.fst,{{modelDir}}\\number-zh.fst\" --kokoro-length-scale={lengthScale} --tts-silence-scale=0.15 --num-threads=4 --sid={{voice}} --output-filename=\"{{output}}\" \"{{text}}\"";
            }
        }
        else if (SelectedSpeechSynthesizerId == "windows-tts")
        {
            var config = _providers.GetConfig("windows-tts");
            config.DisplayName = "Windows TTS";
            config.Endpoint = "";
            config.Model = "system-speech";
            config.AuthRef = "";
        }
    }

    private void ApplyAsrRuntimeConfig(string providerName, string providerCode, string baseUrl, string authRef)
    {
        var model = Required(AsrModel, providerCode.Equals("aliyun", StringComparison.OrdinalIgnoreCase) ? "qwen3-asr-flash-realtime" : "whisper-1");
        if (providerCode.Equals("aliyun", StringComparison.OrdinalIgnoreCase) &&
            model.Contains("qwen3-asr", StringComparison.OrdinalIgnoreCase) &&
            model.Contains("realtime", StringComparison.OrdinalIgnoreCase))
        {
            var config = _providers.GetConfig("openai-asr");
            var workspaceId = ResolveAliyunWorkspaceIdForRealtime();
            config.DisplayName = $"{providerName} ASR";
            config.Endpoint = BuildAliyunQwenAsrRealtimeEndpoint(model, workspaceId);
            config.Model = model;
            config.AuthRef = "llm-aliyun";
            config.Options["baseUrl"] = baseUrl;
            config.Options["path"] = "/api-ws/v1/realtime";
            config.Options["llmProviderCode"] = providerCode;
            config.Options["providerMode"] = "aliyun-qwen-realtime";
            config.Options["workspaceId"] = workspaceId;
            return;
        }

        ApplyCompatibleRuntimeConfig("openai-asr", $"{providerName} ASR", baseUrl, "/v1/audio/transcriptions", model, authRef);
        var asrConfig = _providers.GetConfig("openai-asr");
        asrConfig.Options.Remove("providerMode");
        asrConfig.Options.Remove("workspaceId");
    }

    private string ResolveAliyunWorkspaceIdForRealtime()
    {
        var workspaceId = TtsWorkspaceId.Trim();
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            var aliyunTts = _providers.GetConfig("aliyun-qwen-tts");
            workspaceId = aliyunTts.Options.GetValueOrDefault("workspaceId", "").Trim();
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                workspaceId = ExtractAliyunWorkspaceId(aliyunTts.Options.GetValueOrDefault("realtimeEndpoint", aliyunTts.Endpoint));
            }
        }

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            throw new InvalidOperationException("阿里百炼 Qwen ASR Realtime 需要业务空间 ID。请在“检索与语音”的空间 ID 输入框填写并保存；TTS 和 ASR realtime 共用这个空间 ID。");
        }

        return workspaceId;
    }

    private static string BuildAliyunQwenAsrRealtimeEndpoint(string model, string workspaceId) =>
        $"wss://{workspaceId.Trim()}.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime?model={Uri.EscapeDataString(model.Trim())}";

    private static string BuildAliyunQwenTtsEndpoint(string model, string workspaceId)
    {
        if (!model.Contains("realtime", StringComparison.OrdinalIgnoreCase))
        {
            return "https://dashscope.aliyuncs.com/api/v1/services/aigc/multimodal-generation/generation";
        }

        var id = workspaceId.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("阿里百炼 Realtime TTS 需要填写业务空间 ID；非 realtime 模型不需要填写。");
        }

        return $"wss://{id}.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime?model={{model}}";
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

    private void ApplyCompatibleRuntimeConfig(string providerId, string displayName, string baseUrl, string path, string model, string authRef)
    {
        var config = _providers.GetConfig(providerId);
        config.DisplayName = displayName;
        config.Endpoint = LlmProviderCatalogService.BuildCompatibleEndpoint(baseUrl, path);
        config.Model = model;
        config.AuthRef = authRef;
        config.Options["baseUrl"] = baseUrl;
        config.Options["path"] = path;
        config.Options["llmProviderCode"] = LlmSelectedProviderCode;
    }

    private void ApplyLlmProviderFormToSettings(bool saveApiKey)
    {
        if (string.IsNullOrWhiteSpace(LlmSelectedProviderCode))
        {
            return;
        }

        var provider = _llmProviderCatalog.GetProvider(LlmSelectedProviderCode);
        var chatConfig = _providers.GetConfig("openai-chat");
        var effectiveBaseUrl = LlmProviderCatalogService.ResolveBaseUrlForProvider(provider.Code, LlmBaseUrl);
        LlmBaseUrl = effectiveBaseUrl;
        foreach (var (key, value) in provider.DefaultOptions)
        {
            chatConfig.Options[key] = value;
        }
        chatConfig.Options["enableThinking"] = LlmEnableThinking ? "true" : "false";

        LlmProviderCatalogService.ApplyProviderToChatConfig(
            chatConfig,
            provider.Code,
            Required(effectiveBaseUrl, provider.DefaultBaseUrl),
            Required(LlmSelectedModel, provider.RecommendModels.FirstOrDefault() ?? ""),
            LlmModelOptions.ToArray());
        _settings.ChatProviderId = "openai-chat";
        SelectedChatProviderId = "openai-chat";

        if (saveApiKey && !string.IsNullOrWhiteSpace(ReadableApiKeyInput(LlmApiKeyToSave)))
        {
            _credentialService.WriteSecret(LlmProviderCatalogService.CredentialName(provider.Code), ReadableApiKeyInput(LlmApiKeyToSave));
        }
    }

    partial void OnLlmSelectedProviderCodeChanged(string value)
    {
        if (_loadingLlmProviderForm || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var provider = _llmProviderCatalog.GetProvider(value);
        var chatConfig = _providers.GetConfig("openai-chat");
        LlmBaseUrl = LlmProviderCatalogService.ResolveBaseUrlForProvider(provider.Code, "");
        LlmSelectedModel = provider.RecommendModels.FirstOrDefault() ?? "";
        LlmEnableThinking = false;
        LlmApiKeyToSave = _credentialService.ReadSecret(LlmProviderCatalogService.CredentialName(provider.Code)) is null
            ? ""
            : MaskedApiKey;
        RefreshLlmModelOptions(provider, chatConfig, []);
        RefreshRuntimeCapabilityModelOptions([]);
        LlmModelStatus = $"已切换到 {provider.Name}，请填写 API Key 并保存。";
    }

    partial void OnSelectedSpeechSynthesizerIdChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        LoadRuntimeCapabilityModels();
        if (!_initializingRuntimeProviderForm)
        {
            _ = SaveRuntimeCapabilitySettingsAsync();
        }
    }

    partial void OnTtsModelChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            _initializingRuntimeProviderForm ||
            !SelectedSpeechSynthesizerId.Equals("local-vits-tts", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TtsVoice = NormalizeLocalVitsVoiceForUi(value, TtsVoice);
    }

    partial void OnSelectedEmbeddingProviderIdChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        LoadRuntimeCapabilityModels();
        if (!_initializingRuntimeProviderForm)
        {
            _ = SaveRuntimeCapabilitySettingsAsync();
        }
    }

    partial void OnSelectedSpeechRecognizerIdChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        LoadRuntimeCapabilityModels();
        if (!_initializingRuntimeProviderForm)
        {
            _ = SaveRuntimeCapabilitySettingsAsync();
        }
    }

    private static string ExtractBaseUrl(string endpoint, string fallback)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return fallback;
        }

        var markers = new[] { "/chat/completions", "/v1/chat/completions" };
        foreach (var marker in markers)
        {
            var index = endpoint.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index > 0)
            {
                return endpoint[..index].TrimEnd('/');
            }
        }

        return fallback;
    }

    private static string ReadableApiKeyInput(string value)
    {
        var trimmed = (value ?? "").Trim();
        return IsMaskedApiKey(trimmed) ? "" : trimmed;
    }

    private static bool IsMaskedOrFilledApiKey(string value) =>
        !string.IsNullOrWhiteSpace(value);

    private static bool IsMaskedApiKey(string value) =>
        value.StartsWith("***", StringComparison.Ordinal);

    private void RefreshQuickQuestions()
    {
        QuickQuestions.Clear();
        foreach (var question in _settings.QuickQuestions.Where(question => !string.IsNullOrWhiteSpace(question)).Take(8))
        {
            QuickQuestions.Add(new QuickQuestionItem(question));
        }
    }

    private void RefreshAssistantTags()
    {
        AssistantTags.Clear();
        foreach (var tag in _settings.AssistantTags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Take(6))
        {
            AssistantTags.Add(new AssistantTagItem(tag));
        }
    }

    private static string HashPin(string pin)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(pin.Trim()));
        return Convert.ToHexString(hash);
    }

    private static string Required(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string FormatWorldBook(IEnumerable<WorldBookEntry> entries)
    {
        return string.Join(Environment.NewLine, entries.Select(entry =>
        {
            var keywords = string.Join(",", entry.Keywords);
            return $"{entry.Name} | {keywords} => {entry.Content}";
        }));
    }

    private static List<WorldBookEntry> ParseWorldBook(string text)
    {
        var entries = new List<WorldBookEntry>();
        foreach (var rawLine in text.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            var arrowIndex = line.IndexOf("=>", StringComparison.Ordinal);
            if (arrowIndex < 0)
            {
                entries.Add(new WorldBookEntry
                {
                    Name = $"世界书 {entries.Count + 1}",
                    Content = line
                });
                continue;
            }

            var left = line[..arrowIndex].Trim();
            var content = line[(arrowIndex + 2)..].Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            var name = $"世界书 {entries.Count + 1}";
            var keywordsPart = left;
            var separatorIndex = left.IndexOf('|');
            if (separatorIndex >= 0)
            {
                name = Required(left[..separatorIndex], name);
                keywordsPart = left[(separatorIndex + 1)..];
            }

            var keywords = keywordsPart
                .Split([",", "，", ";", "；"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(16)
                .ToList();

            entries.Add(new WorldBookEntry
            {
                Name = name,
                Keywords = keywords,
                Content = content
            });
        }

        return entries.Take(50).ToList();
    }

    private static string ResolvePetPreviewImagePath(AppSettings settings)
    {
        var frame = FirstFrame(settings.PetFramesDirectory);
        if (!string.IsNullOrWhiteSpace(frame))
        {
            return frame;
        }

        var image = ExpandPath(settings.PetImagePath);
        return File.Exists(image) ? image : "";
    }

    private static string ResolveLogoPreviewImagePath(AppSettings settings)
    {
        var logo = ExpandPath(settings.LogoImagePath);
        return File.Exists(logo) ? logo : "";
    }

    private static string GetAsrTestInitialDirectory()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Assets", "TestAudio");
        return Directory.Exists(directory)
            ? directory
            : AppContext.BaseDirectory;
    }

    private static string FirstFrame(string directory)
    {
        var expanded = ExpandPath(directory);
        if (!Directory.Exists(expanded))
        {
            return "";
        }

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp"
        };

        return Directory.EnumerateFiles(expanded)
            .Where(path => extensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? "";
    }

    private static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path ?? "").Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(expanded) || Path.IsPathRooted(expanded))
        {
            return expanded;
        }

        return Path.Combine(AppContext.BaseDirectory, expanded);
    }

    private void AddLog(string level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        Logs.Insert(0, entry);
        WriteLogFile(entry);
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
    }

    private void OnProviderDiagnosticLogged(string level, string message)
    {
        if (System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true)
        {
            AddLog(level, message);
            return;
        }

        _ = System.Windows.Application.Current?.Dispatcher?.BeginInvoke(() => AddLog(level, message));
    }

    private void WriteLogFile(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogsPath);
            var logPath = Path.Combine(_paths.LogsPath, $"app-{entry.Timestamp:yyyyMMdd}.log");
            var text = $"""
            [{entry.Timestamp:O}] [{entry.Level}]
            {entry.Message}
            ---

            """;
            File.AppendAllText(logPath, text, Encoding.UTF8);
        }
        catch
        {
            // Logging must never break the kiosk interaction flow.
        }
    }

    private void AddExceptionLog(string title, Exception ex)
    {
        AddLog("Error", $"""
        {title}
        Type: {ex.GetType().FullName}
        Message: {ex.Message}
        Details:
        {ex}
        """);
    }

    private void LogRuntimeSnapshot(string operation)
    {
        try
        {
            var chat = _providers.GetConfig(_settings.ChatProviderId);
            var embedding = _providers.GetConfig(_settings.EmbeddingProviderId);
            var asr = _providers.GetConfig(_settings.SpeechRecognizerId);
            var tts = _providers.GetConfig(_settings.SpeechSynthesizerId);
            AddLog("Info", $"""
            Runtime snapshot: {operation}
            Chat: provider={chat.ProviderId}, display={chat.DisplayName}, model={chat.Model}, endpoint={chat.Endpoint}, authRef={chat.AuthRef}
            Embedding: provider={embedding.ProviderId}, display={embedding.DisplayName}, model={embedding.Model}, endpoint={embedding.Endpoint}, authRef={embedding.AuthRef}
            ASR: provider={asr.ProviderId}, display={asr.DisplayName}, model={asr.Model}, endpoint={asr.Endpoint}, authRef={asr.AuthRef}
            TTS: provider={tts.ProviderId}, display={tts.DisplayName}, model={tts.Model}, endpoint={tts.Endpoint}, authRef={tts.AuthRef}, voice={tts.Options.GetValueOrDefault("voice", "")}, instructions={TrimLogValue(tts.Options.GetValueOrDefault("instructions", ""), 220)}
            """);
        }
        catch (Exception ex)
        {
            AddLog("Error", $"Failed to capture runtime snapshot: {ex.Message}");
        }
    }

    private static string TrimLogValue(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Length <= maxLength ? value : value[..maxLength] + "...";

    public ValueTask DisposeAsync()
    {
        ProviderDiagnostics.Logged -= OnProviderDiagnosticLogged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        return ValueTask.CompletedTask;
    }
}
