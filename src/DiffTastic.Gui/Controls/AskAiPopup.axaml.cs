using Avalonia.Controls;
using Avalonia.Input;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Controls;

public sealed partial class AskAiPopup : UserControl
{
    public AskAiPopup()
    {
        InitializeComponent();
        QuestionBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || DataContext is not AskAiViewModel vm)
                return;
            if (vm.AskCommand.CanExecute(null))
                vm.AskCommand.Execute(null);
            e.Handled = true;
        };
    }

    public void FocusQuestion() => QuestionBox.Focus();
}
