using System.Drawing;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class SnapDeciderTests
{
    private static readonly AppSettings TestSettings = new()
    {
        ThrowVelocityThresholdPxPerSec = 1500,
        HorizontalDominanceRatio = 1.75
    };

    [Fact]
    public void Decide_ReturnsLeftHalf_ForFastLeftThrow()
    {
        var decider = new SnapDecider();
        var session = CreateSession((400, 80, 0), (120, 95, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(SnapTarget.LeftHalf, decision.Target);
        Assert.True(decision.HorizontalVelocityPxPerSec < 0);
    }

    [Fact]
    public void Decide_ReturnsRightHalf_ForFastRightThrow()
    {
        var decider = new SnapDecider();
        var session = CreateSession((0, 50, 0), (220, 65, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(SnapTarget.RightHalf, decision.Target);
        Assert.True(decision.HorizontalVelocityPxPerSec > 0);
    }

    [Fact]
    public void Decide_RejectsSlowDrag()
    {
        var decider = new SnapDecider();
        var session = CreateSession((0, 20, 0), (120, 30, 300));

        var decision = decider.Decide(session, TestSettings);

        Assert.False(decision.IsQualified);
        Assert.Equal(SnapTarget.None, decision.Target);
    }

    [Fact]
    public void Decide_RejectsMostlyVerticalDrag()
    {
        var decider = new SnapDecider();
        var session = CreateSession((0, 0, 0), (200, 160, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.False(decision.IsQualified);
        Assert.Equal(SnapTarget.None, decision.Target);
    }

    private static DragSession CreateSession((int X, int Y, int Ms) first, (int X, int Y, int Ms) second)
    {
        var monitor = new MonitorInfo(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
        var session = new DragSession(new IntPtr(1), monitor, new Rectangle(100, 100, 800, 600));
        var origin = DateTimeOffset.UtcNow;

        session.AddSample(new DragSample(new Point(first.X, first.Y), origin.AddMilliseconds(first.Ms)));
        session.AddSample(new DragSample(new Point(second.X, second.Y), origin.AddMilliseconds(second.Ms)));
        return session;
    }
}
