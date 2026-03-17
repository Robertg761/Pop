using System.Drawing;
using System.Windows.Threading;
using Pop.Core.Interfaces;
using Pop.Core.Models;

namespace Pop.App.Services;

public sealed class WpfOverlayPresenter(Dispatcher dispatcher) : IOverlayPresenter, IDisposable
{
    private readonly Dispatcher _dispatcher = dispatcher;
    private OverlayWindow? _overlayWindow;
    private bool _isVisible;
    private SnapTarget _lastTarget = SnapTarget.None;
    private Rectangle _lastBounds = Rectangle.Empty;

    public void Update(SnapTarget target, Rectangle bounds)
    {
        if (_isVisible && _lastTarget == target && _lastBounds == bounds)
        {
            return;
        }

        InvokeOnDispatcher(() =>
        {
            var overlayWindow = EnsureWindow();
            overlayWindow.UpdatePreview(bounds, target);
            _isVisible = true;
            _lastTarget = target;
            _lastBounds = bounds;
        });
    }

    public void Hide()
    {
        if (!_isVisible)
        {
            return;
        }

        InvokeOnDispatcher(() =>
        {
            if (_overlayWindow is not null)
            {
                _overlayWindow.HidePreview();
            }

            _isVisible = false;
            _lastTarget = SnapTarget.None;
            _lastBounds = Rectangle.Empty;
        });
    }

    public void Dispose()
    {
        InvokeOnDispatcher(() =>
        {
            if (_overlayWindow is not null)
            {
                _overlayWindow.ClosePermanently();
                _overlayWindow = null;
            }

            _isVisible = false;
            _lastTarget = SnapTarget.None;
            _lastBounds = Rectangle.Empty;
        });
    }

    private OverlayWindow EnsureWindow()
    {
        _overlayWindow ??= new OverlayWindow();
        return _overlayWindow;
    }

    private void InvokeOnDispatcher(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }
}
