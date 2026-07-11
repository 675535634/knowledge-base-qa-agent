namespace KnowledgeBaseQaAgent.Desktop.Services;

public static class ProviderDiagnostics
{
    public static event Action<string, string>? Logged;

    public static void Info(string message) => Logged?.Invoke("Info", message);

    public static void Error(string message) => Logged?.Invoke("Error", message);
}
