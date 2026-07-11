using System.Globalization;
using System.Speech.Recognition;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class WakeWordService : IDisposable
{
    private SpeechRecognitionEngine? _recognizer;
    private bool _disposed;

    public event EventHandler<string>? WakeWordDetected;

    public bool TryStart(IEnumerable<string> wakeWords)
    {
        try
        {
            var words = wakeWords
                .Where(word => !string.IsNullOrWhiteSpace(word))
                .Select(word => word.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (words.Length == 0)
            {
                return false;
            }

            _recognizer = new SpeechRecognitionEngine(CultureInfo.CurrentCulture);
            var choices = new Choices(words);
            var grammar = new Grammar(new GrammarBuilder(choices))
            {
                Name = "WakeWords"
            };
            _recognizer.LoadGrammar(grammar);
            _recognizer.SetInputToDefaultAudioDevice();
            _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
            _recognizer.RecognizeAsync(RecognizeMode.Multiple);
            return true;
        }
        catch
        {
            Dispose();
            return false;
        }
    }

    private void Recognizer_SpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (e.Result.Confidence >= 0.45)
        {
            WakeWordDetected?.Invoke(this, e.Result.Text);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_recognizer is null)
        {
            return;
        }

        try
        {
            _recognizer.RecognizeAsyncCancel();
            _recognizer.SpeechRecognized -= Recognizer_SpeechRecognized;
            _recognizer.Dispose();
        }
        catch
        {
            // Wake word support is opportunistic; disposal failures are non-fatal.
        }
    }
}
