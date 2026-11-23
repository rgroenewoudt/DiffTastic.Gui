using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using DiffTastic.Core.Diff;
using TextMateSharp.Grammars;

namespace DiffTastic.Gui.Controls;

/// <summary>One side of the diff: title bar, line numbers and a read-only editor.</summary>
public sealed class DiffPane : DockPanel
{
    private readonly TextBlock _title;
    private readonly Border _header;
    private readonly Border _badge;
    private readonly DiffBackgroundRenderer _renderer;
    private readonly RowNumberMargin _margin;
    private readonly TextMate.Installation _textMate;
    private readonly FoldingManager _folding;
    private IReadOnlyList<FoldRegion> _regions = [];
    private readonly List<FoldingSection> _sections = [];
    private bool[] _knownStates = [];

    private static readonly RegistryOptions Registry = new(ThemeName.LightPlus);

    public DiffPane(Side side)
    {
        Side = side;

        _title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _badge = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 0),
            Child = new TextBlock { Text = side == Side.Left ? "A" : "B", Foreground = Brushes.White, FontWeight = FontWeight.Bold },
        };
        _header = new Border
        {
            Padding = new Thickness(8, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    _badge,
                    _title,
                },
            },
        };
        SetDock(_header, Dock.Top);
        Children.Add(_header);

        Editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 13,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = side == Side.Left ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto,
            Padding = new Thickness(4, 0, 0, 0),
        };
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.AllowScrollBelowDocument = false;
        Editor.TextArea.TextView.CurrentLineBackground = Brushes.Transparent;
        Editor.TextArea.TextView.CurrentLineBorder = null;

        _renderer = new DiffBackgroundRenderer(side);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_renderer);

        _margin = new RowNumberMargin(side);
        Editor.TextArea.LeftMargins.Insert(0, _margin);

        Children.Add(Editor);

        _textMate = Editor.InstallTextMate(Registry);

        ApplyTheme();
        ActualThemeVariantChanged += (_, _) => ApplyTheme();

        _folding = FoldingManager.Install(Editor.TextArea);
        Editor.TextArea.TextView.VisualLinesChanged += (_, _) =>
        {
            if (!GetFoldStates().SequenceEqual(_knownStates))
            {
                RememberFoldStates();
                FoldsToggled?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    private void ApplyTheme()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var colors = DiffColors.For(ActualThemeVariant);
        _textMate.SetTheme(Registry.LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
        _header.Background = Side == Side.Left ? colors.LeftHeader : colors.RightHeader;
        _badge.Background = Side == Side.Left ? colors.RemovedMarker : colors.AddedMarker;
        Editor.Background = colors.EditorBackground;
        Editor.SearchResultsBrush = colors.SearchResult;
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>Raised after the user opened or closed a folding.</summary>
    public event EventHandler? FoldsToggled;

    public Side Side { get; }
    public TextEditor Editor { get; }
    public IReadOnlyList<AlignedRow> Rows { get; private set; } = [];

    public string Title => _title.Text ?? "";

    public void SetTitle(string title, string? fullPath)
    {
        _title.Text = title;
        ToolTip.SetTip(_title, fullPath);
    }

    public void Show(IReadOnlyList<AlignedRow> rows)
    {
        Rows = rows;
        _renderer.Rows = rows;
        _margin.Rows = rows;

        var sb = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            if (i > 0)
                sb.Append('\n');
            sb.Append((Side == Side.Left ? rows[i].Left : rows[i].Right)?.Text.Replace('\r', LineAligner.CarriageReturnSymbol));
        }
        Editor.Text = sb.ToString();
        Editor.TextArea.TextView.Redraw();
    }

    /// <summary>Replaces all foldings. Regions are in row space, so both panes fold the same rows.</summary>
    public void SetFoldings(IReadOnlyList<FoldRegion> regions, IReadOnlySet<FoldRegion> folded)
    {
        _folding.Clear();
        _sections.Clear();
        _regions = regions;
        var document = Editor.Document;
        foreach (var region in regions)
        {
            var start = document.GetLineByNumber(region.StartRow + 1).EndOffset;
            var end = document.GetLineByNumber(region.EndRow + 1).EndOffset;
            var section = _folding.CreateFolding(start, end);
            section.IsFolded = folded.Contains(region);
            _sections.Add(section);
        }
        RememberFoldStates();
    }

    public bool[] GetFoldStates() => _sections.Select(s => s.IsFolded).ToArray();

    public void ApplyFoldStates(bool[] states)
    {
        for (var i = 0; i < Math.Min(states.Length, _sections.Count); i++)
            _sections[i].IsFolded = states[i];
        RememberFoldStates();
    }

    /// <summary>Opens every folding that hides a row in first..last.</summary>
    public void UnfoldRows(int first, int last)
    {
        for (var i = 0; i < _sections.Count; i++)
        {
            if (_regions[i].StartRow < last && first <= _regions[i].EndRow)
                _sections[i].IsFolded = false;
        }
        RememberFoldStates();
    }

    private void RememberFoldStates() => _knownStates = GetFoldStates();

    /// <summary>Picks a syntax grammar from a file name; plain text when unknown.</summary>
    public void SetLanguageFromFileName(string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? "");
        var language = string.IsNullOrEmpty(extension) ? null : Registry.GetLanguageByExtension(extension);
        _textMate.SetGrammar(language is null ? null : Registry.GetScopeByLanguageId(language.Id));
    }

    public void SetCurrentBlock(DiffBlock? block)
    {
        _renderer.CurrentBlock = block;
        Editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
    }

    /// <summary>Rows that get an AI review hint marker in the gutter.</summary>
    public void SetHintRows(IReadOnlySet<int> rows) => _margin.HintRows = rows;

    public IReadOnlySet<int> HintRows => _margin.HintRows;

    private ScrollViewer? ScrollViewer => Editor.FindDescendantOfType<ScrollViewer>();

    public double VerticalOffset => ScrollViewer?.Offset.Y ?? 0;

    public void ScrollTo(double? x, double y)
    {
        if (ScrollViewer is { } sv)
            sv.Offset = new Vector(x ?? sv.Offset.X, y);
    }

    /// <summary>Row of a 1-based line number on this side; the nearest row when the line does not exist, or -1.</summary>
    public int RowOfLine(int lineNumber)
    {
        var last = -1;
        for (var i = 0; i < Rows.Count; i++)
        {
            if ((Side == Side.Left ? Rows[i].Left : Rows[i].Right) is not { } line)
                continue;
            if (line.LineIndex + 1 >= lineNumber)
                return i;
            last = i;
        }
        return last;
    }

    /// <summary>Moves the caret to the start of a row and focuses the editor.</summary>
    public void SetCaretRow(int row)
    {
        Editor.TextArea.ClearSelection();
        Editor.TextArea.Caret.Line = row + 1;
        Editor.TextArea.Caret.Column = 1;
        Editor.TextArea.Focus();
    }

    /// <summary>Row index under a point relative to this pane's text view, or -1.</summary>
    public int RowAt(Point pointInTextView)
    {
        var view = Editor.TextArea.TextView;
        var line = view.GetDocumentLineByVisualTop(pointInTextView.Y + view.VerticalOffset);
        return line is null ? -1 : line.LineNumber - 1;
    }
}
