using System.Drawing;
using System.Windows.Threading;
using Pop.Core.Interfaces;
using Pop.Core.Models;

namespace Pop.App.Services;

public sealed class WpfOverlayPresenter(Dispatcher dispatcher) : IOverlayPresenter, IDisposable
{
    private readonly Dispatcher _dispatcher = dispatcher;
    private OverlayWindow? _overlayWindow;

    public void Show(SnapTarget target, Rectangle bounds)
    {
        _dispatcher.Invoke(() =>
        {
            var overlayWindow = EnsureWindow();
            overlayWindow.UpdateBounds(bounds, target);

            if (!overlayWindow.IsVisible)
            {
                overlayWindow.Show();
            }
        });
    }

    public void Hide()
    {
        _dispatcher.Invoke(() =>
        {
            if (_overlayWindow is { IsVisible: true })
            {
                _overlayWindow.Hide();
            }
        });
    }

    public void Dispose()
    {
        _dispatcher.Invoke(() =>
        {
            if (_overlayWindow is not null)
            {
                _overlayWindow.ClosePermanently();
                _overlayWindow = null;
            }
        });
    }

    private OverlayWindow EnsureWindow()
    {
        _overlayWindow ??= new OverlayWindow();
        return _overlayWindow;
    }
}
