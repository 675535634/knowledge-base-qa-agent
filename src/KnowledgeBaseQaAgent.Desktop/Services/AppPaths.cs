namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class AppPaths
{
    public const string PortableFlagFileName = "portable.flag";

    public AppPaths(string? root = null, bool isPortable = false)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KnowledgeBaseQaAgent");
        IsPortable = isPortable;
        DatabasePath = Path.Combine(Root, "knowledge.db");
        SettingsPath = Path.Combine(Root, "settings.json");
        SecretsPath = Path.Combine(Root, "secrets.json");
        SecretKeyPath = Path.Combine(Root, "secret.key");
        LogsPath = Path.Combine(Root, "logs");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LogsPath);
    }

    public static AppPaths FromStartupArgs(IReadOnlyList<string> args)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var portableFlagPath = Path.Combine(baseDirectory, PortableFlagFileName);
        var portableByArg = args.Any(arg => arg.Equals("--portable", StringComparison.OrdinalIgnoreCase));
        if (portableByArg || File.Exists(portableFlagPath))
        {
            return new AppPaths(Path.Combine(baseDirectory, "Data"), isPortable: true);
        }

        return new AppPaths();
    }

    public string Root { get; }
    public bool IsPortable { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }
    public string SecretsPath { get; }
    public string SecretKeyPath { get; }
    public string LogsPath { get; }

    public string ModeDescription =>
        IsPortable
            ? $"便携模式，数据目录：{Root}"
            : $"安装模式，数据目录：{Root}";
}
