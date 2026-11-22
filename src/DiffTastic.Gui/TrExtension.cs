using Avalonia;
using Avalonia.Data;

namespace DiffTastic.Gui;

/// <summary><c>{l:Tr Key}</c>: text from Strings.resx that updates when the language changes.</summary>
public sealed class TrExtension(string key)
{
    public string Key { get; } = key;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new LocalizedText(Key).ToBinding();

    private sealed class LocalizedText(string key) : IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            void OnChanged(object? sender, EventArgs e) => observer.OnNext(Localization.Get(key));
            Localization.LanguageChanged += OnChanged;
            observer.OnNext(Localization.Get(key));
            return new Unsubscriber(() => Localization.LanguageChanged -= OnChanged);
        }
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
