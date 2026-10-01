using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TremorScope.Core.Privacy;

namespace TremorScope.Infrastructure.Settings;

/// <summary>
/// 秘密を暗号化・復号する仕組み。
/// アプリでは Windows の DPAPI（ログインしているユーザーだけが復号できる）を使う。
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] protectedData);
}

/// <summary>
/// 設定を保存する。
/// settings.json … 秘密ではない設定（読みやすい JSON）
/// secrets.bin   … 接続文字列・鍵（<see cref="ISecretProtector"/> で暗号化）
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string directory;
    private readonly ISecretProtector protector;

    public SettingsStore(string directory, ISecretProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = directory;
        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public string SettingsPath => Path.Combine(directory, "settings.json");
    public string SecretsPath => Path.Combine(directory, "secrets.bin");
    public string DatabasePath => Path.Combine(directory, "tremorscope.db");

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            return (JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? new AppSettings()).Normalized();
        }
        catch (JsonException)
        {
            // 壊れていたら既定値に戻す（元のファイルは残しておく）
            File.Copy(SettingsPath, SettingsPath + ".broken", overwrite: true);
            return new AppSettings();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WriteAtomically(SettingsPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings.Normalized(), Json)));
    }

    public AppSecrets LoadSecrets()
    {
        if (!File.Exists(SecretsPath)) return new AppSecrets();
        byte[] plain = protector.Unprotect(File.ReadAllBytes(SecretsPath));
        try
        {
            return JsonSerializer.Deserialize<AppSecrets>(plain, Json) ?? new AppSecrets();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void SaveSecrets(AppSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(secrets, Json);
        try
        {
            WriteAtomically(SecretsPath, protector.Protect(plain));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>施設の鍵を読む。初めての起動なら作って保存する</summary>
    public byte[] GetOrCreateSiteKey()
    {
        var secrets = LoadSecrets();
        if (!string.IsNullOrEmpty(secrets.SiteKey))
        {
            byte[] key = Convert.FromBase64String(secrets.SiteKey);
            if (key.Length >= 16) return key;
        }
        byte[] created = Pseudonymizer.CreateSiteKey();
        SaveSecrets(secrets with { SiteKey = Convert.ToBase64String(created) });
        return created;
    }

    private void WriteAtomically(string path, byte[] content)
    {
        Directory.CreateDirectory(directory);
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
