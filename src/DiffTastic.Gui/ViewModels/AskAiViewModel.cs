using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiffTastic.Core.Ai;
using DiffTastic.Gui.Resources;

namespace DiffTastic.Gui.ViewModels;

/// <summary>A free question to the AI about a difference block or a selection.</summary>
public sealed partial class AskAiViewModel(DiffQuestioner questioner, string fileName, string context, string contextLabel)
    : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public string ContextLabel => contextLabel;

    public string Context => context;

    public string ToolTip => Localization.Format(Strings.AskAiTip, questioner.Provider.Name, questioner.Provider.Vendor);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    private string _question = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnswer))]
    private string? _answer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
    private bool _isAsking;

    public bool HasAnswer => Answer is not null;

    private bool CanAsk() => !IsAsking && !_disposed && !string.IsNullOrWhiteSpace(Question);

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync()
    {
        var ct = _cts.Token;
        IsAsking = true;
        Answer = Strings.Thinking;
        try
        {
            var question = Question;
            Answer = await Task.Run(() => questioner.AskAsync(fileName, context, question, ct), ct);
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                Answer = Localization.Format(Strings.AiFailed, ex.Message.Split('\n', 2)[0].Trim());
        }
        finally
        {
            if (!_disposed)
                IsAsking = false;
        }
    }

    /// <summary>Stops a running question; called when the popup goes away.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
    }
}
