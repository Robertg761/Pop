using System.Drawing;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class OverlayStateTrackerTests
{
    [Fact]
    public void Evaluate_AvoidsRedundantShowAndHideTransitions()
    {
        var tracker = new OverlayStateTracker();
        var monitor = new MonitorInfo(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
        var qualified = new SnapDecision(SnapTarget.LeftHalf, -2500, 120, 3, true, SnapRejectionReason.None);
        var unqualified = SnapDecision.None(SnapRejectionReason.InsufficientVelocity, -400, 20, 20);

        var first = tracker.Evaluate(qualified, monitor, overlayEnabled: true);
        var second = tracker.Evaluate(qualified, monitor, overlayEnabled: true);
        var third = tracker.Evaluate(unqualified, monitor, overlayEnabled: true);
        var fourth = tracker.Evaluate(unqualified, monitor, overlayEnabled: true);

        Assert.Equal(OverlayTransitionAction.ShowOrUpdate, first.Action);
        Assert.Equal(OverlayTransitionAction.None, second.Action);
        Assert.Equal(OverlayTransitionAction.Hide, third.Action);
        Assert.Equal(OverlayTransitionAction.None, fourth.Action);
    }
}
