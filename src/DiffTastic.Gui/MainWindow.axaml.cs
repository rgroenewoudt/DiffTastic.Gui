using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Search;
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
    private (string? Left, string? Right)? _shownPaths;
    private UnifiedLayout? _unified;
    private GridLength _leftWidth = new(1, GridUnitType.Star);

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
            view.PointerPressed += (_, e) => _vm?.SelectBlockAtRow(SourceRow(pane.RowAt(e.GetPosition(view))));
            pane.FoldsToggled += (_, _) => (pane == LeftPane ? RightPane : LeftPane).ApplyFoldStates(pane.GetFoldStates());
        }

        Blame = new BlameHoverController(PanesGrid, CreateBlamePopupAsync);
        Blame.Attach(LeftPane);
        Blame.Attach(RightPane);
        Search = new DiffSearch(LeftPane, RightPane, row => ScrollToRow(row, center: false));

        Overview.RowRequested += (_, row) => ScrollToRow(row, center: true);
        OpenButton.Click += async (_, _) => await OpenFilesAsync();
        ReloadButton.Click += async (_, _) => await (_vm?.LoadAsync() ?? Task.CompletedTask);
        ExpandAllButton.Click += (_, _) => ExpandAll();
        CollapseUnchangedButton.Click += (_, _) => CollapseUnchanged();
        SettingsButton.Click += async (_, _) => await ShowSettingsAsync();
        GoToLineBox.KeyDown += OnGoToLineKeyDown;
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Blame.Close();
        UpdateTitles();
    }

    private Task<BlamePopupViewModel?> CreateBlamePopupAsync(Side side, int row)
    {
        if (_unified is { } unified && row >= 0 && row < unified.Rows.Count)
            (side, row) = (UnifiedRow.SideOf(unified.Rows[row]), unified.SourceRow(row));
        return _vm?.CreateBlamePopupAsync(side, row) ?? Task.FromResult<BlamePopupViewModel?>(null);
    }

    private IReadOnlyList<DiffBlock> ShownBlocks => _unified?.Blocks ?? _vm?.Document?.Blocks ?? [];

    /// <summary>Row of the document for a shown row; differs in the unified view.</summary>
    private int SourceRow(int row) => row < 0 || _unified is null ? row : _unified.SourceRow(row);

    private int ShownRow(int sourceRow) => _unified?.RowOf(sourceRow) ?? sourceRow;

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
    public DiffSearch Search { get; }

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
            ShowWhitespace();
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
                ShowCurrentBlock(scroll: true);
                break;
            case nameof(MainWindowViewModel.LeftTitle) or nameof(MainWindowViewModel.RightTitle):
                UpdateTitles();
                break;
            case nameof(MainWindowViewModel.Analysis) or nameof(MainWindowViewModel.WhitespaceIsNoise):
                ShowAnalysis();
                break;
            case nameof(MainWindowViewModel.ShowWhitespace):
                ShowWhitespace();
                break;
            case nameof(MainWindowViewModel.UnifiedView):
                ShowDocument();
                break;
        }
    }

    private void ShowAnalysis()
    {
        var blocks = ShownBlocks;
        var notes = _vm?.Analysis?.Hunks
            .Where(n => n.Hunk < blocks.Count)
            .Select(n => (Block: blocks[n.Hunk], Note: n))
            .ToList() ?? [];
        Overview.Notes = notes;
        Overview.NoiseBlocks = blocks.Where((_, i) => _vm!.IsNoise(i)).ToList();
        var hintRows = notes.Where(n => n.Note.ReviewHints.Count > 0).Select(n => n.Block.StartRow).ToHashSet();
        LeftPane.SetHintRows(hintRows);
        RightPane.SetHintRows(hintRows);
    }

    private void ShowWhitespace()
    {
        LeftPane.ShowWhitespace = _vm?.ShowWhitespace ?? false;
        RightPane.ShowWhitespace = _vm?.ShowWhitespace ?? false;
    }

    private void ShowDocument()
    {
        Blame.Close();
        var paths = (_vm?.LeftPath, _vm?.RightPath);
        int? keepRow = _shownPaths == paths ? SourceRow(AnchorRow()) : null;
        _shownPaths = _vm?.Document is null ? null : paths;
        _unified = _vm is { UnifiedView: true, Document: { } doc } ? new UnifiedLayout(doc) : null;
        ShowLayout(_unified is not null);
        var rows = _unified?.Rows ?? _vm?.Document?.Rows ?? [];
        var fileName = _vm?.RepoPath ?? _vm?.RightPath;
        LeftPane.SetLanguageFromFileName(fileName);
        RightPane.SetLanguageFromFileName(fileName);
        LeftPane.Show(_unified is null ? rows : []);
        RightPane.Show(rows, unified: _unified is not null);
        _foldRegions = FoldRegionFinder.Find(rows);
        _defaultFolded = FoldRegionFinder.DefaultFolded(_foldRegions).ToHashSet();
        LeftPane.SetFoldings(_unified is null ? _foldRegions : [], _defaultFolded);
        RightPane.SetFoldings(_foldRegions, _defaultFolded);
        Overview.Rows = rows;
        ShowAnalysis();
        UpdateTitles();
        ShowCurrentBlock(scroll: keepRow is null);
        if (keepRow is { } row)
            ScrollToRow(ShownRow(row), center: false);
    }

    /// <summary>Unified: only the right pane, at full width.</summary>
    private void ShowLayout(bool unified)
    {
        if (unified != LeftPane.IsVisible)
            return;
        var columns = PanesGrid.ColumnDefinitions;
        if (unified)
            _leftWidth = columns[0].Width;
        columns[0].Width = unified ? new GridLength(0) : _leftWidth;
        columns[1].Width = new GridLength(unified ? 0 : 4);
        LeftPane.IsVisible = !unified;
        PanesSplitter.IsVisible = !unified;
    }

    /// <summary>Row at a third of the viewport, where <see cref="ScrollToRow(int, bool)"/> puts a row.</summary>
    private int AnchorRow()
    {
        var view = RightPane.Editor.TextArea.TextView;
        var line = view.GetDocumentLineByVisualTop(view.VerticalOffset + RightPane.Editor.ViewportHeight / 3);
        return (line?.LineNumber ?? 1) - 1;
    }

    private void UpdateTitles()
    {
        LeftPane.SetTitle(_vm?.LeftTitle ?? "", _vm?.LeftPath);
        if (_unified is null)
            RightPane.SetTitle(_vm?.RightTitle ?? "", _vm?.RightPath);
        else
            RightPane.SetTitle($"{_vm?.LeftTitle} → {_vm?.RightTitle}", $"{_vm?.LeftPath}\n{_vm?.RightPath}");
        var name = _vm?.RepoPath ?? (_vm?.RightPath is { } p ? Path.GetFileName(p) : null);
        Title = name is null ? "DiffTastic" : $"{name} - DiffTastic";
    }

    private void ShowCurrentBlock(bool scroll)
    {
        var blocks = ShownBlocks;
        DiffBlock? block = _vm is { CurrentBlock: var i } && i >= 0 && i < blocks.Count ? blocks[i] : null;
        LeftPane.SetCurrentBlock(block);
        RightPane.SetCurrentBlock(block);
        if (block is { } b && scroll)
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
        else if (e.Key == Key.F && ctrl)
        {
            Search.Open(LeftPane.IsKeyboardFocusWithin ? LeftPane : RightPane.IsKeyboardFocusWithin ? RightPane : null);
            e.Handled = true;
        }
        else if (e.Key == Key.G && ctrl)
        {
            OpenGoToLine(LeftPane.IsKeyboardFocusWithin ? LeftPane : RightPane.IsKeyboardFocusWithin ? RightPane : null);
            e.Handled = true;
        }
        else if (e.Key == Key.F3 || (e.Key == Key.Enter && e.Source is Visual v && v.FindAncestorOfType<SearchPanel>() is not null))
        {
            if (Search.IsOpen)
                Search.FindNext(backwards: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = Search.IsOpen;
        }
        else if (e.Key == Key.F5)
        {
            _ = _vm.LoadAsync();
            e.Handled = true;
        }
    }

    /// <summary>Shows the go-to-line bar for the given pane (right pane when null).</summary>
    public void OpenGoToLine(DiffPane? pane)
    {
        (pane == LeftPane ? GoToLeft : GoToRight).IsChecked = true;
        GoToLineBar.IsVisible = true;
        GoToLineBox.SelectAll();
        GoToLineBox.Focus();
    }

    /// <summary>Scrolls to a 1-based line number of one side and puts the caret there.</summary>
    public void GoToLine(Side side, int lineNumber)
    {
        var pane = side == Side.Left && _unified is null ? LeftPane : RightPane;
        var row = pane.RowOfLine(lineNumber, side);
        if (row < 0)
            return;
        ScrollToRow(row, center: true);
        pane.SetCaretRow(row);
    }

    private void OnGoToLineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (int.TryParse(GoToLineBox.Text, out var line))
            {
                GoToLineBar.IsVisible = false;
                GoToLine(GoToLeft.IsChecked == true ? Side.Left : Side.Right, line);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            GoToLineBar.IsVisible = false;
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
