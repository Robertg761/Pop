using System.Drawing;
using Pop.Core.Models;

namespace Pop.Core.Services;

public sealed class OverlayStateTracker
{
    private bool _isVisible;
    private SnapTarget _target;
    private Rectangle _bounds;

    public OverlayTransition Evaluate(SnapDecision decision, MonitorInfo monitorInfo, bool overlayEnabled)
    {
        if (!overlayEnabled || !decision.IsQualified)
        {
            return Reset();
        }

        var bounds = TileLayoutCalculator.GetTileBounds(decision.Target, monitorInfo);
        if (bounds == Rectangle.Empty)
        {
            return Reset();
        }

        if (_isVisible && _target == decision.Target && _bounds == bounds)
        {
            return new OverlayTransition(OverlayTransitionAction.None, _target, _bounds);
        }

        _isVisible = true;
        _target = decision.Target;
        _bounds = bounds;
        return new OverlayTransition(OverlayTransitionAction.ShowOrUpdate, decision.Target, bounds);
    }

    public OverlayTransition Reset()
    {
        if (!_isVisible)
        {
            return new OverlayTransition(OverlayTransitionAction.None, SnapTarget.None, Rectangle.Empty);
        }

        _isVisible = false;
        _target = SnapTarget.None;
        _bounds = Rectangle.Empty;
        return new OverlayTransition(OverlayTransitionAction.Hide, SnapTarget.None, Rectangle.Empty);
    }
}
