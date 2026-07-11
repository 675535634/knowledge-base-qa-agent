using System.Security.Cryptography;
using System.Text.Json;

namespace KnowledgeBaseQaAgent.Desktop.Services;

public sealed class PortableCredentialService : ICredentialService
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public PortableCredentialService(AppPaths paths)
    {
        _paths = paths;
    }

    public string StorageDescription => "便携目录 Data\\secrets.json（AES 加密）";

    public string? ReadSecret(string targetName)
    {
        lock (_gate)
        {
            return Load().GetValueOrDefault(targetName);
        }
    }

    public void WriteSecret(string targetName, string secret)
    {
        lock (_gate)
        {
            var secrets = Load();
            secrets[targetName] = secret;
            Save(secrets);
        }
    }

    public void DeleteSecret(string targetName)
    {
        lock (_gate)
        {
            var secrets = Load();
            if (secrets.Remove(targetName))
            {
                Save(secrets);
            }
        }
    }

    private Dictionary<string, string> Load()
    {
        if (!File.Exists(_paths.SecretsPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = File.OpenRead(_paths.SecretsPath);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.TryGetProperty("version", out var version) &&
            version.GetString() == "portable-secrets-v1")
        {
            return DecryptContainer(document.RootElement);
        }

        var plaintext = document.RootElement.Deserialize<Dictionary<string, string>>(JsonOptions)
            ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(plaintext, StringComparer.OrdinalIgnoreCase);
    }

    private void Save(Dictionary<string, string> secrets)
    {
        Directory.CreateDirectory(_paths.Root);
        var tempPath = _paths.SecretsPath + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            JsonSerializer.Serialize(stream, EncryptContainer(secrets), JsonOptions);
        }

        if (File.Exists(_paths.SecretsPath))
        {
            File.Replace(tempPath, _paths.SecretsPath, null);
        }
        else
        {
            File.Move(tempPath, _paths.SecretsPath);
        }
    }

    private PortableSecretContainer EncryptContainer(Dictionary<string, string> secrets)
    {
        var key = LoadOrCreateKey();
        var encrypted = new Dictionary<string, PortableSecretValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var (targetName, secret) in secrets)
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var plaintext = System.Text.Encoding.UTF8.GetBytes(secret);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSizeBytes];
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
            encrypted[targetName] = new PortableSecretValue(
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext),
                Convert.ToBase64String(tag));
        }

        return new PortableSecretContainer("portable-secrets-v1", encrypted);
    }

    private Dictionary<string, string> DecryptContainer(JsonElement root)
    {
        var key = LoadOrCreateKey();
        var values = root.GetProperty("secrets").Deserialize<Dictionary<string, PortableSecretValue>>(JsonOptions)
            ?? new Dictionary<string, PortableSecretValue>();
        var decrypted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (targetName, encrypted) in values)
        {
            var nonce = Convert.FromBase64String(encrypted.Nonce);
            var ciphertext = Convert.FromBase64String(encrypted.Ciphertext);
            var tag = Convert.FromBase64String(encrypted.Tag);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSizeBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            decrypted[targetName] = System.Text.Encoding.UTF8.GetString(plaintext);
        }

        return decrypted;
    }

    private byte[] LoadOrCreateKey()
    {
        Directory.CreateDirectory(_paths.Root);
        if (File.Exists(_paths.SecretKeyPath))
        {
            var encoded = File.ReadAllText(_paths.SecretKeyPath).Trim();
            return Convert.FromBase64String(encoded);
        }

        var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        File.WriteAllText(_paths.SecretKeyPath, Convert.ToBase64String(key));
        return key;
    }

    private sealed record PortableSecretContainer(
        string Version,
        Dictionary<string, PortableSecretValue> Secrets);

    private sealed record PortableSecretValue(
        string Nonce,
        string Ciphertext,
        string Tag);
}
