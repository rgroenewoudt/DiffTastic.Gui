using Avalonia.Controls;

namespace DiffTastic.Gui;

/// <summary>Dialog that closes with true on OK and false on Cancel.</summary>
public sealed partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }
}
