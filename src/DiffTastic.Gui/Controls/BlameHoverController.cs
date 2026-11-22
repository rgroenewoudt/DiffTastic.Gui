using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Threading;
using DiffTastic.Gui.ViewModels;

namespace DiffTastic.Gui.Controls;

/// <summary>Shows the blame popup when the pointer rests on a line, and keeps it open while the pointer is on it.</summary>
public sealed class BlameHoverController
{
    private readonly Popup _popup;
    private readonly BlamePopup _content = new();
    private readonly DispatcherTimer _closeTimer;
    private readonly Func<Side, int, Task<BlamePopupViewModel?>> _factory;
    private DiffPane? _pane;
    private int _row = -1;
    private int _request;

    public BlameHoverController(Panel host, Func<Side, int, Task<BlamePopupViewModel?>> factory)
    {
        _factory = factory;
        _popup = new Popup
        {
            Child = _content,
            ShouldUseOverlayLayer = true,
            IsLightDismissEnabled = false,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.TopRight,
            HorizontalOffset = -24,
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.SlideX,
        };
        host.Children.Add(_popup);

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _closeTimer.Tick += (_, _) => Close();

        _content.PointerEntered += (_, _) => _closeTimer.Stop();
        _content.PointerExited += (_, _) => _closeTimer.Start();
    }

    public bool IsOpen => _popup.IsOpen;
    public BlamePopupViewModel? Current => _content.DataContext as BlamePopupViewModel;
    public Control Content => _content;

    public void Attach(DiffPane pane)
    {
        var view = pane.Editor.TextArea.TextView;
        view.PointerHover += async (_, e) =>
        {
            var p = e.GetPosition(view);
            try
            {
                await ShowAsync(pane, pane.RowAt(p), p.X);
            }
            catch (Exception)
            {
                Close();
            }
        };
        view.PointerMoved += (_, e) =>
        {
            if (IsOpen && (pane != _pane || pane.RowAt(e.GetPosition(view)) != _row))
                _closeTimer.Start();
        };
        view.PointerExited += (_, _) =>
        {
            if (IsOpen)
                _closeTimer.Start();
        };
        view.ScrollOffsetChanged += (_, _) => Close();
    }

    public async Task ShowAsync(DiffPane pane, int row, double x)
    {
        if (IsOpen && pane == _pane && row == _row)
        {
            _closeTimer.Stop();
            return;
        }

        var request = ++_request;
        var vm = await _factory(pane.Side, row);
        if (request != _request)
            return;
        if (vm is null)
        {
            Close();
            return;
        }

        _closeTimer.Stop();
        _pane = pane;
        _row = row;
        Current?.Dispose();
        _content.DataContext = vm;

        var view = pane.Editor.TextArea.TextView;
        var top = view.GetVisualTopByDocumentLine(Math.Max(1, row + 1)) - view.VerticalOffset;
        _popup.PlacementTarget = view;
        _popup.PlacementRect = new Rect(x, top, 1, view.DefaultLineHeight);
        _popup.IsOpen = true;

        await vm.LoadAsync();
    }

    public void Close()
    {
        _closeTimer.Stop();
        _request++;
        _popup.IsOpen = false;
        Current?.Dispose();
        _pane = null;
        _row = -1;
    }
}
