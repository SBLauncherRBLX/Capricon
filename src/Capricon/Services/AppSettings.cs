using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Capricon.Services;

/// <summary>User settings in %APPDATA%\Capricon\settings.json. The API key is encrypted with DPAPI.</summary>
public sealed class AppSettings
{
    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capricon");

    static string FilePath => Path.Combine(Folder, "settings.json");

    public string ApiKeyProtected { get; set; } = "";
    public string Model { get; set; } = "claude-opus-5-5";
    public string Effort { get; set; } = "low";
    public bool Muted { get; set; }
    public string VoiceName { get; set; } = "";
    public int VoiceRate { get; set; } = 1;
    public int VoicePitch { get; set; } = 25;
    public double Scale { get; set; } = 0.9;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool ReactClipboard { get; set; } = true;
    public bool ReactIdle { get; set; } = true;
    public bool ReactSystem { get; set; } = true;
    public bool Chatter { get; set; } = true;
    public bool FollowCursor { get; set; } = true;

    /// <summary>Plain API key: the saved one, else the ANTHROPIC_API_KEY environment variable.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ApiKey
    {
        get
        {
            if (ApiKeyProtected.Length > 0)
            {
                try
                {
                    var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ApiKeyProtected), null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch (CryptographicException)
                {
                    // Copied from another Windows account: fall through to the environment.
                }
            }
            return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";
        }
        set => ApiKeyProtected = string.IsNullOrWhiteSpace(value)
            ? ""
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    }

    public static AppSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
