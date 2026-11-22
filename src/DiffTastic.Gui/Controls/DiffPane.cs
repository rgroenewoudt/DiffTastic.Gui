using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using DiffTastic.Core.Diff;
using TextMateSharp.Grammars;

namespace DiffTastic.Gui.Controls;

/// <summary>One side of the diff: title bar, line numbers and a read-only editor.</summary>
public sealed class DiffPane : DockPanel
{
    private readonly TextBlock _title;
    private readonly DiffBackgroundRenderer _renderer;
    private readonly RowNumberMargin _margin;
    private readonly TextMate.Installation _textMate;

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
        var header = new Border
        {
            Background = side == Side.Left ? DiffColors.LeftHeader : DiffColors.RightHeader,
            Padding = new Thickness(8, 4),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new Border
                    {
                        Background = side == Side.Left ? DiffColors.RemovedMarker : DiffColors.AddedMarker,
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(6, 0),
                        Child = new TextBlock { Text = side == Side.Left ? "A" : "B", Foreground = Brushes.White, FontWeight = FontWeight.Bold },
                    },
                    _title,
                },
            },
        };
        SetDock(header, Dock.Top);
        Children.Add(header);

        Editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 13,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = side == Side.Left ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto,
            Background = Brushes.White,
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
    }

    public Side Side { get; }
    public TextEditor Editor { get; }
    public IReadOnlyList<AlignedRow> Rows { get; private set; } = [];

    public string Title
    {
        get => _title.Text ?? "";
        set => _title.Text = value;
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
            sb.Append((Side == Side.Left ? rows[i].Left : rows[i].Right)?.Text);
        }
        Editor.Text = sb.ToString();
        Editor.TextArea.TextView.Redraw();
    }

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

    /// <summary>Row index under a point relative to this pane's text view, or -1.</summary>
    public int RowAt(Point pointInTextView)
    {
        var view = Editor.TextArea.TextView;
        var line = view.GetDocumentLineByVisualTop(pointInTextView.Y + view.VerticalOffset);
        return line is null ? -1 : line.LineNumber - 1;
    }
}
