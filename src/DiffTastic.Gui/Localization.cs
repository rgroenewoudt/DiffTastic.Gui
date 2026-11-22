using System.Globalization;
using DiffTastic.Gui.Resources;

namespace DiffTastic.Gui;

/// <summary>Current UI language. Text in XAML (<see cref="TrExtension"/>) follows <see cref="LanguageChanged"/>.</summary>
public static class Localization
{
    public static readonly IReadOnlyList<string> Supported = ["en", "de", "nl", "fr"];

    public static CultureInfo SystemCulture { get; set; } = CultureInfo.CurrentUICulture;

    public static string Current { get; private set; } = "en";

    /// <summary>Language name for AI prompts, for example "Dutch".</summary>
    public static string AiLanguage => CultureInfo.GetCultureInfo(Current).EnglishName;

    public static event EventHandler? LanguageChanged;

    /// <summary>Supported code for a setting; null follows the system language, falling back to English.</summary>
    public static string Resolve(string? code)
    {
        var wanted = code ?? SystemCulture.TwoLetterISOLanguageName;
        return Supported.FirstOrDefault(s => string.Equals(s, wanted, StringComparison.OrdinalIgnoreCase)) ?? "en";
    }

    public static void Apply(string? code)
    {
        var resolved = Resolve(code);
        var culture = CultureInfo.GetCultureInfo(resolved);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Strings.Culture = culture;
        if (resolved == Current)
            return;
        Current = resolved;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key) => Strings.ResourceManager.GetString(key, Strings.Culture) ?? key;

    public static string Format(string format, params object?[] args) => string.Format(Strings.Culture, format, args);
}
