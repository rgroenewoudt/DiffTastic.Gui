using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DiffTastic.Core.Diff;
using DiffTastic.Gui.Controls;
using DiffTastic.Gui.Resources;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui;

public sealed partial class MainWindow : Window
{
    private bool _syncing;
    private MainWindowViewModel? _vm;
    private IReadOnlyList<FoldRegion> _foldRegions = [];
    private HashSet<FoldRegion> _defaultFolded = [];

    public MainWindow()
    {
        InitializeComponent();

        LeftPane = new DiffPane(Side.Left);
        RightPane = new DiffPane(Side.Right);
        Grid.SetColumn(RightPane, 2);
        PanesGrid.Children.Insert(0, LeftPane);
        PanesGrid.Children.Insert(1, RightPane);

        foreach (var pane in new[] { LeftPane, RightPane })
        {
            var view = pane.Editor.TextArea.TextView;
            view.ScrollOffsetChanged += (_, _) => SyncScroll(pane);
            view.VisualLinesChanged += (_, _) => UpdateViewport();
            view.PointerPressed += (_, e) => _vm?.SelectBlockAtRow(pane.RowAt(e.GetPosition(view)));
            pane.FoldsToggled += (_, _) => (pane == LeftPane ? RightPane : LeftPane).ApplyFoldStates(pane.GetFoldStates());
        }

        Blame = new BlameHoverController(PanesGrid, (side, row) =>
            _vm?.CreateBlamePopupAsync(side, row) ?? Task.FromResult<BlamePopupViewModel?>(null));
        Blame.Attach(LeftPane);
        Blame.Attach(RightPane);

        Overview.RowRequested += (_, row) => ScrollToRow(row, center: true);
        OpenButton.Click += async (_, _) => await OpenFilesAsync();
        ReloadButton.Click += async (_, _) => await (_vm?.LoadAsync() ?? Task.CompletedTask);
        ExpandAllButton.Click += (_, _) => ExpandAll();
        CollapseUnchangedButton.Click += (_, _) => CollapseUnchanged();
        SettingsButton.Click += async (_, _) => await ShowSettingsAsync();
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Blame.Close();
        UpdateTitles();
    }

    public void ExpandAll() => ApplyFoldStates(new bool[_foldRegions.Count]);

    public void CollapseUnchanged() => ApplyFoldStates(_foldRegions.Select(_defaultFolded.Contains).ToArray());

    private void ApplyFoldStates(bool[] states)
    {
        LeftPane.ApplyFoldStates(states);
        RightPane.ApplyFoldStates(states);
    }

    public DiffPane LeftPane { get; }
    public DiffPane RightPane { get; }
    public BlameHoverController Blame { get; }

    protected override void OnClosed(EventArgs e)
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        Blame.Close();
        _vm?.CancelAnalysis();
        base.OnClosed(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null)
            _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as MainWindowViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelChanged;
            ShowDocument();
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowViewModel.Document):
                ShowDocument();
                break;
            case nameof(MainWindowViewModel.CurrentBlock):
                ShowCurrentBlock();
                break;
            case nameof(MainWindowViewModel.LeftTitle) or nameof(MainWindowViewModel.RightTitle):
                UpdateTitles();
                break;
            case nameof(MainWindowViewModel.Analysis):
                ShowAnalysis();
                break;
        }
    }

    private void ShowAnalysis()
    {
        var blocks = _vm?.Document?.Blocks ?? [];
        var notes = _vm?.Analysis?.Hunks
            .Where(n => n.Hunk < blocks.Count)
            .Select(n => (Block: blocks[n.Hunk], Note: n))
            .ToList() ?? [];
        Overview.Notes = notes;
        var hintRows = notes.Where(n => n.Note.ReviewHints.Count > 0).Select(n => n.Block.StartRow).ToHashSet();
        LeftPane.SetHintRows(hintRows);
        RightPane.SetHintRows(hintRows);
    }

    private void ShowDocument()
    {
        Blame.Close();
        var rows = _vm?.Document?.Rows ?? [];
        var fileName = _vm?.RepoPath ?? _vm?.RightPath;
        LeftPane.SetLanguageFromFileName(fileName);
        RightPane.SetLanguageFromFileName(fileName);
        LeftPane.Show(rows);
        RightPane.Show(rows);
        _foldRegions = FoldRegionFinder.Find(rows);
        _defaultFolded = FoldRegionFinder.DefaultFolded(_foldRegions).ToHashSet();
        LeftPane.SetFoldings(_foldRegions, _defaultFolded);
        RightPane.SetFoldings(_foldRegions, _defaultFolded);
        Overview.Rows = rows;
        ShowAnalysis();
        UpdateTitles();
        ShowCurrentBlock();
    }

    private void UpdateTitles()
    {
        LeftPane.SetTitle(_vm?.LeftTitle ?? "", _vm?.LeftPath);
        RightPane.SetTitle(_vm?.RightTitle ?? "", _vm?.RightPath);
        var name = _vm?.RepoPath ?? (_vm?.RightPath is { } p ? Path.GetFileName(p) : null);
        Title = name is null ? "DiffTastic" : $"{name} - DiffTastic";
    }

    private void ShowCurrentBlock()
    {
        DiffBlock? block = _vm is { Document: { } doc, CurrentBlock: var i } && i >= 0 && i < doc.Blocks.Count
            ? doc.Blocks[i]
            : null;
        LeftPane.SetCurrentBlock(block);
        RightPane.SetCurrentBlock(block);
        if (block is { } b)
        {
            LeftPane.UnfoldRows(b.StartRow, b.EndRow);
            RightPane.UnfoldRows(b.StartRow, b.EndRow);
            ScrollToRow(b.StartRow, center: false);
        }
    }

    /// <summary>Scrolls both panes so the row is visible near the top third (or centered).</summary>
    public void ScrollToRow(int row, bool center)
    {
        LeftPane.UnfoldRows(row, row);
        RightPane.UnfoldRows(row, row);
        ScrollToRow(row, center, attempts: 5);
    }

    private void ScrollToRow(int row, bool center, int attempts)
    {
        var view = RightPane.Editor.TextArea.TextView;
        var lineHeight = view.DefaultLineHeight;
        var viewport = RightPane.Editor.ViewportHeight;
        var top = view.GetVisualTopByDocumentLine(Math.Clamp(row + 1, 1, Math.Max(1, view.Document?.LineCount ?? 1)));
        var layoutPending = viewport <= 0 || RightPane.Editor.ExtentHeight < top;
        if (layoutPending && attempts > 0)
        {
            Dispatcher.UIThread.Post(() => ScrollToRow(row, center, attempts - 1), DispatcherPriority.Background);
            return;
        }

        if (!center && top >= view.VerticalOffset + lineHeight && top + lineHeight * 3 <= view.VerticalOffset + viewport)
            return;

        var offset = Math.Max(0, top - (center ? viewport / 2 : viewport / 3));
        RightPane.ScrollTo(null, offset);
        LeftPane.ScrollTo(null, offset);

        if (Math.Abs(RightPane.VerticalOffset - offset) > 1 && attempts > 0)
            Dispatcher.UIThread.Post(() => ScrollToRow(row, center, attempts - 1), DispatcherPriority.Background);
    }

    private void SyncScroll(DiffPane source)
    {
        if (_syncing)
            return;
        _syncing = true;
        try
        {
            var target = source == LeftPane ? RightPane : LeftPane;
            var offset = source.Editor.TextArea.TextView.ScrollOffset;
            target.ScrollTo(offset.X, offset.Y);
        }
        finally
        {
            _syncing = false;
        }
        UpdateViewport();
    }

    private void UpdateViewport()
    {
        var view = RightPane.Editor.TextArea.TextView;
        if (view.DefaultLineHeight <= 0 || view.Document is null)
            return;
        var first = view.GetDocumentLineByVisualTop(view.VerticalOffset)?.LineNumber - 1 ?? 0;
        var last = view.GetDocumentLineByVisualTop(view.VerticalOffset + view.Bounds.Height)?.LineNumber ?? first;
        Overview.Viewport = (first, last);
    }

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
            return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        System.Windows.Input.ICommand? command = (e.Key, ctrl) switch
        {
            (Key.Down, true) => _vm.NextDiffCommand,
            (Key.Up, true) => _vm.PreviousDiffCommand,
            (Key.Home, true) => _vm.FirstDiffCommand,
            (Key.End, true) => _vm.LastDiffCommand,
            _ => null,
        };

        if (command is not null)
        {
            if (command.CanExecute(null))
                command.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Blame.IsOpen)
        {
            Blame.Close();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            _ = _vm.LoadAsync();
            e.Handled = true;
        }
    }

    private async Task ShowSettingsAsync()
    {
        if (_vm is null)
            return;
        var settings = new SettingsViewModel(_vm.Settings);
        if (await new SettingsWindow { DataContext = settings }.ShowDialog<bool>(this))
        {
            settings.ApplyTo(_vm.Settings);
            _vm.ApplySettings();
        }
    }

    private async Task OpenFilesAsync()
    {
        if (_vm is null)
            return;

        var left = await PickFileAsync(Strings.OpenLeftTitle);
        if (left is null)
            return;
        var right = await PickFileAsync(Strings.OpenRightTitle);
        if (right is null)
            return;

        await _vm.OpenAsync(left, right);
    }

    private async Task<string?> PickFileAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
