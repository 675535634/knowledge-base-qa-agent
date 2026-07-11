namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class SpeechCoordinator
{
    private readonly ProviderRegistry _providers;

    public SpeechCoordinator(ProviderRegistry providers)
    {
        _providers = providers;
    }

    public Task<string> RecognizeOnceAsync(CancellationToken cancellationToken) =>
        _providers.CreateSpeechRecognizer().RecognizeOnceAsync(cancellationToken);

    public Task<string> TranscribeFileAsync(string audioPath, CancellationToken cancellationToken) =>
        _providers.CreateAudioTranscriber().TranscribeFileAsync(audioPath, cancellationToken);

    public Task SpeakAsync(string text, CancellationToken cancellationToken) =>
        _providers.CreateSpeechSynthesizer().SpeakAsync(text, cancellationToken);
}
