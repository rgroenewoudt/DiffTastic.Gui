using CommunityToolkit.Mvvm.ComponentModel;
using DiffTastic.Core.Ai;

namespace DiffTastic.Gui.ViewModels;

/// <summary>Editable copy of <see cref="AppSettings"/> for the settings dialog.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const string Auto = "Auto (first found)";

    public SettingsViewModel(AppSettings settings, Func<string, string?>? findOnPath = null)
    {
        var find = findOnPath ?? (name => AiLocator.FindOnPath(name));
        ClaudeFound = find(AiLocator.Claude) is not null;
        CodexFound = find(AiLocator.Codex) is not null;

        _aiEnabled = settings.AiEnabled;
        _provider = settings.AiProvider?.ToLowerInvariant() switch
        {
            AiLocator.Claude => "Claude",
            AiLocator.Codex => "Codex",
            _ => Auto,
        };
        _claudeModel = settings.ClaudeModel;
        _codexModel = settings.CodexModel;
    }

    public IReadOnlyList<string> Providers { get; } = [Auto, "Claude", "Codex"];

    public bool ClaudeFound { get; }
    public bool CodexFound { get; }
    public bool AnyAiFound => ClaudeFound || CodexFound;

    public string ClaudeStatus => ClaudeFound ? "claude found on PATH" : "claude not found on PATH";
    public string CodexStatus => CodexFound ? "codex found on PATH" : "codex not found on PATH";

    [ObservableProperty] private bool _aiEnabled;
    [ObservableProperty] private string _provider;
    [ObservableProperty] private string? _claudeModel;
    [ObservableProperty] private string? _codexModel;

    public void ApplyTo(AppSettings settings)
    {
        settings.AiEnabled = AiEnabled;
        settings.AiProvider = Provider switch
        {
            "Claude" => AiLocator.Claude,
            "Codex" => AiLocator.Codex,
            _ => null,
        };
        settings.ClaudeModel = Clean(ClaudeModel);
        settings.CodexModel = Clean(CodexModel);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
