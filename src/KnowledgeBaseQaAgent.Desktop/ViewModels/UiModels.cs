namespace KnowledgeBaseQaAgent.Desktop.ViewModels;

using System.Windows;

public sealed record ProviderOption(string ProviderId, string DisplayName);

public sealed record LlmProviderOptionView(string Code, string Name);

public sealed record QuickQuestionItem(string Text);

public sealed record AssistantTagItem(string Text);

public sealed class ConversationItem
{
    public string Role { get; init; } = "";
    public string Content { get; init; } = "";
    public string CitationText { get; init; } = "";
    public string AssistantAvatarPath { get; init; } = "";
    public bool IsThinking { get; init; }
    public bool IsUser => Role.Equals("user", StringComparison.OrdinalIgnoreCase);
    public bool IsAssistant => !IsUser;
    public string RoleDisplay => IsUser ? "我" : "助手";
    public HorizontalAlignment BubbleAlignment => IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public string BubbleBackground => IsUser ? "#EEF0FF" : "#FFFFFF";
    public string AccentColor => IsUser ? "#7A67FF" : "#6370D8";
    public string AvatarSymbol => IsUser ? "✓" : "✦";
    public Visibility UserAvatarVisibility => IsUser ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AssistantAvatarVisibility => IsAssistant && !string.IsNullOrWhiteSpace(AssistantAvatarPath) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AssistantSymbolVisibility => IsAssistant && string.IsNullOrWhiteSpace(AssistantAvatarPath) ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class DocumentItem
{
    public long Id { get; init; }
    public string Title { get; init; } = "";
    public string Path { get; init; } = "";
    public string CreatedAt { get; init; } = "";
}
