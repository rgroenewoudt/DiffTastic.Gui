using System.Text.Json;
using System.Text.Json.Serialization;

namespace DiffTastic.Gui;

/// <summary>User settings in %APPDATA%/DiffTastic/settings.json. Without a file path nothing is saved.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>"en", "de", "nl" or "fr"; null follows the system language.</summary>
    public string? Language { get; set; }

    /// <summary>"light" or "dark"; null follows the system theme.</summary>
    public string? Theme { get; set; }

    public bool AiEnabled { get; set; }

    /// <summary>Shows tabs and spaces as visible marks.</summary>
    public bool ShowWhitespace { get; set; }

    /// <summary>Shows the diff in one column instead of side by side.</summary>
    public bool UnifiedView { get; set; }

    /// <summary>Differences in whitespace only count as noise.</summary>
    public bool WhitespaceIsNoise { get; set; }

    /// <summary>"claude" or "codex"; null picks the first installed one.</summary>
    public string? AiProvider { get; set; }

    public string? ClaudeModel { get; set; }
    public string? CodexModel { get; set; }

    [JsonIgnore]
    public string? FilePath { get; private set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiffTastic", "settings.json");

    public static AppSettings Load(string path)
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        settings ??= new AppSettings();
        settings.FilePath = path;
        return settings;
    }

    public void Save()
    {
        if (FilePath is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
