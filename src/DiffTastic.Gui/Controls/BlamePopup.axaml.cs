using Avalonia.Controls;
using Avalonia.Input.Platform;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Controls;

public partial class BlamePopup : UserControl
{
    public BlamePopup()
    {
        InitializeComponent();
        CopyShaButton.Click += async (_, _) =>
        {
            if (DataContext is BlamePopupViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(vm.Sha);
        };
    }
}
