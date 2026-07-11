using KnowledgeBaseQaAgent.Desktop.Models;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed record ChatTurn(string Role, string Content);

public sealed record ChatRequest(
    string Question,
    IReadOnlyList<SearchResult> Citations,
    IReadOnlyList<ChatTurn> History,
    PromptProfile PromptProfile);

public sealed record AnswerRoute(
    string Mode,
    string DirectAnswer,
    string SearchQuery);

public interface IChatProvider
{
    string ProviderId { get; }
    Task<AnswerRoute> PlanAnswerRouteAsync(string question, IReadOnlyList<ChatTurn> history, PromptProfile promptProfile, CancellationToken cancellationToken);
    Task<string> CreateSearchQueryAsync(string question, IReadOnlyList<ChatTurn> history, PromptProfile promptProfile, CancellationToken cancellationToken);
    Task<string?> CreateFollowUpSearchQueryAsync(string question, IReadOnlyList<SearchResult> citations, IReadOnlyList<ChatTurn> history, PromptProfile promptProfile, CancellationToken cancellationToken);
    Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken);
}

public interface IEmbeddingProvider
{
    string ProviderId { get; }
    int Dimensions { get; }
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);
}

public interface ISpeechRecognizer
{
    string ProviderId { get; }
    Task<string> RecognizeOnceAsync(CancellationToken cancellationToken);
}

public interface IAudioTranscriber
{
    string ProviderId { get; }
    Task<string> TranscribeFileAsync(string audioPath, CancellationToken cancellationToken);
}

public interface ISpeechSynthesizer
{
    string ProviderId { get; }
    Task SpeakAsync(string text, CancellationToken cancellationToken);
}

public interface ICredentialService
{
    string StorageDescription { get; }
    string? ReadSecret(string targetName);
    void WriteSecret(string targetName, string secret);
    void DeleteSecret(string targetName);
}
