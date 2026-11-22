using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DiffTastic.Gui.Resources;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Tests;

[CollectionDefinition(nameof(GlobalLanguage), DisableParallelization = true)]
public sealed class GlobalLanguage;

[Collection(nameof(GlobalLanguage))]
public sealed partial class LocalizationTests
{
    private static string Sample(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples", name));

    private static Dictionary<string, string> Texts(string culture) =>
        Strings.ResourceManager.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    [GeneratedRegex(@"\{\d+")]
    private static partial Regex Placeholder();

    [Theory]
    [InlineData("de")]
    [InlineData("nl")]
    [InlineData("fr")]
    public void Every_text_is_translated_with_the_same_placeholders(string culture)
    {
        var english = Texts(CultureInfo.InvariantCulture.Name);
        var translated = Texts(culture);

        Assert.Equal(english.Keys.Order(), translated.Keys.Order());
        foreach (var (key, text) in english)
        {
            var expected = Placeholder().Matches(text).Select(m => m.Value).Distinct().Order();
            var actual = Placeholder().Matches(translated[key]).Select(m => m.Value).Distinct().Order();
            Assert.True(expected.SequenceEqual(actual), $"{culture}: {key}");
        }
    }

    [Theory]
    [InlineData(null, "fr-BE", "fr")]
    [InlineData(null, "nl-NL", "nl")]
    [InlineData(null, "ja-JP", "en")]
    [InlineData("DE", "nl-NL", "de")]
    [InlineData("xx", "nl-NL", "en")]
    public void Resolve_picks_a_supported_language(string? setting, string system, string expected)
    {
        var original = Localization.SystemCulture;
        try
        {
            Localization.SystemCulture = CultureInfo.GetCultureInfo(system);
            Assert.Equal(expected, Localization.Resolve(setting));
        }
        finally
        {
            Localization.SystemCulture = original;
        }
    }

    [AvaloniaFact]
    public void Settings_dialog_edits_the_language()
    {
        var settings = new AppSettings { Language = "de" };
        var vm = new SettingsViewModel(settings, _ => null);
        Assert.Equal("de", vm.Language.Code);

        vm.Language = vm.Languages[0];
        vm.ApplyTo(settings);
        Assert.Null(settings.Language);

        vm.Language = vm.Languages.Single(l => l.Code == "nl");
        vm.ApplyTo(settings);
        Assert.Equal("nl", settings.Language);
    }

    [AvaloniaFact]
    public async Task Language_change_applies_without_restart()
    {
        var settings = new AppSettings();
        var vm = new MainWindowViewModel(new StartupArgs(Sample("old.cs"), Sample("new.cs"), null), settings);
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 600 };
        window.Show();
        await vm.LoadAsync();
        Dispatcher.UIThread.RunJobs();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        try
        {
            settings.Language = "nl";
            vm.ApplySettings();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(string.Empty, changes);
            Assert.StartsWith("Verschil 1 van", vm.StatusText);
            Assert.Equal("Bestanden openen…", ToolTip.GetTip(window.FindControl<Button>("OpenButton")!));
            Screenshots.Save(window, "40-language-nl");

            var dialog = new SettingsWindow { DataContext = new SettingsViewModel(settings, _ => null) };
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Instellingen - DiffTastic", dialog.Title);
            Assert.Equal("nl", ((LanguageOption)dialog.FindControl<ComboBox>("LanguageBox")!.SelectedItem!).Code);
            Screenshots.Save(dialog, "41-settings-nl");
            dialog.Close();
        }
        finally
        {
            settings.Language = "en";
            vm.ApplySettings();
        }
        Assert.StartsWith("Difference 1 of", vm.StatusText);
        Assert.Equal("Open files…", ToolTip.GetTip(window.FindControl<Button>("OpenButton")!));
    }
}
