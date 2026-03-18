using System.Drawing;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class SnapDeciderTests
{
    private static readonly MonitorInfo MainMonitor = new(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
    private static readonly MonitorInfo RightMonitor = new(new Rectangle(1920, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1040));
    private static readonly MonitorInfo LeftMonitor = new(new Rectangle(-1920, 0, 1920, 1080), new Rectangle(-1920, 0, 1920, 1040));
    private static readonly MonitorInfo TopMonitor = new(new Rectangle(0, -1080, 1920, 1080), new Rectangle(0, -1080, 1920, 1040));

    private static readonly AppSettings TestSettings = new()
    {
        ThrowVelocityThresholdPxPerSec = 1500,
        HorizontalDominanceRatio = 1.75
    };

    [Fact]
    public void Decide_ReturnsLeftHalf_ForFastLeftThrow()
    {
        var decider = CreateDecider(MainMonitor);
        var session = CreateSession(MainMonitor, MainMonitor, new Rectangle(100, 100, 800, 600), false, (400, 80, 0), (120, 95, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(SnapTarget.LeftHalf, decision.Target);
        Assert.Equal(MainMonitor, decision.TargetMonitorInfo);
        Assert.True(decision.HorizontalVelocityPxPerSec < 0);
        Assert.Equal(SnapRejectionReason.None, decision.RejectionReason);
    }

    [Fact]
    public void Decide_ReturnsRightHalf_ForFastRightThrow()
    {
        var decider = CreateDecider(MainMonitor);
        var session = CreateSession(MainMonitor, MainMonitor, new Rectangle(100, 100, 800, 600), false, (0, 50, 0), (220, 65, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(SnapTarget.RightHalf, decision.Target);
        Assert.Equal(MainMonitor, decision.TargetMonitorInfo);
        Assert.True(decision.HorizontalVelocityPxPerSec > 0);
        Assert.Equal(SnapRejectionReason.None, decision.RejectionReason);
    }

    [Fact]
    public void Decide_RejectsSlowDrag()
    {
        var decider = CreateDecider(MainMonitor);
        var session = CreateSession(MainMonitor, MainMonitor, new Rectangle(100, 100, 800, 600), false, (0, 20, 0), (120, 30, 300));

        var decision = decider.Decide(session, TestSettings);

        Assert.False(decision.IsQualified);
        Assert.Equal(SnapTarget.None, decision.Target);
        Assert.Equal(SnapRejectionReason.InsufficientVelocity, decision.RejectionReason);
    }

    [Fact]
    public void Decide_RejectsMostlyVerticalDrag()
    {
        var decider = CreateDecider(MainMonitor);
        var session = CreateSession(MainMonitor, MainMonitor, new Rectangle(100, 100, 800, 600), false, (0, 0, 0), (200, 160, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.False(decision.IsQualified);
        Assert.Equal(SnapTarget.None, decision.Target);
        Assert.Equal(SnapRejectionReason.InsufficientHorizontalDominance, decision.RejectionReason);
    }

    [Fact]
    public void Decide_RemainsStable_WhenRecentSamplesStillFavorSameTarget()
    {
        var decider = CreateDecider(MainMonitor);
        var session = new DragSession(new IntPtr(1), MainMonitor, new Rectangle(100, 100, 800, 600));
        var origin = DateTimeOffset.UtcNow;

        session.AddSample(new DragSample(new Point(0, 0), origin));
        session.AddSample(new DragSample(new Point(120, 10), origin.AddMilliseconds(40)));
        session.AddSample(new DragSample(new Point(250, 18), origin.AddMilliseconds(80)));
        session.UpdateCurrentBounds(new Rectangle(100, 100, 800, 600));
        session.UpdateCurrentMonitorInfo(MainMonitor);

        var firstDecision = decider.Decide(session, TestSettings);

        session.AddSample(new DragSample(new Point(360, 16), origin.AddMilliseconds(110)));
        var secondDecision = decider.Decide(session, TestSettings);

        Assert.Equal(SnapTarget.RightHalf, firstDecision.Target);
        Assert.Equal(SnapTarget.RightHalf, secondDecision.Target);
        Assert.True(secondDecision.IsQualified);
    }

    [Fact]
    public void Decide_WithCtrl_FastCrossMonitorThrow_UsesLandingSideOnDestinationMonitor()
    {
        var decider = CreateDecider(MainMonitor, RightMonitor);
        var session = CreateSession(
            MainMonitor,
            MainMonitor,
            new Rectangle(2150, 100, 800, 600),
            true,
            (0, 0, 0),
            (300, 20, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(RightMonitor, decision.TargetMonitorInfo);
        Assert.Equal(SnapTarget.RightHalf, decision.Target);
        Assert.True(decision.ProjectedLandingPoint.X > RightMonitor.WorkArea.Left + (RightMonitor.WorkArea.Width / 2));
    }

    [Fact]
    public void Decide_WithCtrl_SlowCrossMonitorThrowToRight_UsesSideClosestToSource()
    {
        var decider = CreateDecider(MainMonitor, RightMonitor);
        var session = CreateSession(
            MainMonitor,
            MainMonitor,
            new Rectangle(2600, 100, 800, 600),
            true,
            (0, 0, 0),
            (40, 5, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(RightMonitor, decision.TargetMonitorInfo);
        Assert.Equal(SnapTarget.LeftHalf, decision.Target);
        Assert.True(decision.ProjectedLandingPoint.X > RightMonitor.WorkArea.Left + (RightMonitor.WorkArea.Width / 2));
    }

    [Fact]
    public void Decide_WithCtrl_SlowCrossMonitorThrowToLeft_UsesSideClosestToSource()
    {
        var decider = CreateDecider(LeftMonitor, MainMonitor);
        var session = CreateSession(
            MainMonitor,
            MainMonitor,
            new Rectangle(-1700, 100, 800, 600),
            true,
            (40, 5, 0),
            (0, 0, 100));

        session.UpdateCurrentMonitorInfo(MainMonitor);

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(LeftMonitor, decision.TargetMonitorInfo);
        Assert.Equal(SnapTarget.RightHalf, decision.Target);
        Assert.True(decision.ProjectedLandingPoint.X < LeftMonitor.WorkArea.Left + (LeftMonitor.WorkArea.Width / 2));
    }

    [Fact]
    public void Decide_WithCtrl_VerticalThrowToStackedMonitor_UsesDominantAxisSpeed()
    {
        var decider = CreateDecider(MainMonitor, TopMonitor);
        var session = CreateSession(
            MainMonitor,
            MainMonitor,
            new Rectangle(1000, 50, 800, 600),
            true,
            (0, 0, 0),
            (15, -200, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(TopMonitor, decision.TargetMonitorInfo);
        Assert.Equal(SnapTarget.RightHalf, decision.Target);
        Assert.InRange(decision.ProjectedLandingPoint.X, TopMonitor.Bounds.Left, TopMonitor.Bounds.Right - 1);
        Assert.InRange(decision.ProjectedLandingPoint.Y, TopMonitor.Bounds.Top, TopMonitor.Bounds.Bottom - 1);
        Assert.True(decision.ProjectedLandingPoint.X > TopMonitor.WorkArea.Left + (TopMonitor.WorkArea.Width / 2));
    }

    [Fact]
    public void Decide_WithCtrl_StackedMonitorFallsBackToLandingX()
    {
        var decider = CreateDecider(MainMonitor, TopMonitor);
        var session = CreateSession(
            MainMonitor,
            MainMonitor,
            new Rectangle(1000, -290, 800, 600),
            true,
            (0, 0, 0),
            (50, -8, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(TopMonitor, decision.TargetMonitorInfo);
        Assert.Equal(SnapTarget.RightHalf, decision.Target);
        Assert.True(decision.ProjectedLandingPoint.Y < MainMonitor.Bounds.Top);
    }

    [Fact]
    public void Decide_WithCtrl_DownwardThrowFromTopMonitor_UsesLowerMonitor()
    {
        var decider = CreateDecider(TopMonitor, MainMonitor);
        var session = CreateSession(
            TopMonitor,
            TopMonitor,
            new Rectangle(1000, -700, 800, 600),
            true,
            (0, 0, 0),
            (12, 40, 100));

        var decision = decider.Decide(session, TestSettings);

        Assert.True(decision.IsQualified);
        Assert.Equal(MainMonitor, decision.TargetMonitorInfo);
        Assert.Equal(SnapTarget.RightHalf, decision.Target);
        Assert.True(decision.ProjectedLandingPoint.Y < MainMonitor.Bounds.Bottom);
    }

    private static SnapDecider CreateDecider(params MonitorInfo[] monitors)
    {
        return new SnapDecider(new MonitorLookup(monitors).InspectMonitorAt);
    }

    private static DragSession CreateSession(
        MonitorInfo initialMonitor,
        MonitorInfo currentMonitor,
        Rectangle currentBounds,
        bool ctrlAtRelease,
        params (int X, int Y, int Ms)[] samples)
    {
        var session = new DragSession(new IntPtr(1), initialMonitor, new Rectangle(100, 100, 800, 600));
        var origin = DateTimeOffset.UtcNow;

        DragSample? lastSample = null;
        foreach (var sample in samples)
        {
            lastSample = new DragSample(new Point(sample.X, sample.Y), origin.AddMilliseconds(sample.Ms));
            session.AddSample(lastSample.Value);
        }

        session.UpdateCurrentBounds(currentBounds);
        session.UpdateCurrentMonitorInfo(currentMonitor);

        if (lastSample.HasValue)
        {
            session.CompleteRelease(lastSample.Value, ctrlAtRelease);
        }

        return session;
    }

    private sealed class MonitorLookup(params MonitorInfo[] monitors)
    {
        private readonly IReadOnlyList<MonitorInfo> _monitors = monitors;

        public MonitorInfo InspectMonitorAt(Point screenPoint)
        {
            foreach (var monitor in _monitors)
            {
                if (monitor.Bounds.Contains(screenPoint))
                {
                    return monitor;
                }
            }

            return _monitors
                .OrderBy(monitor => GetDistanceSquared(screenPoint, monitor.Bounds))
                .FirstOrDefault(MonitorInfo.Empty);
        }

        private static long GetDistanceSquared(Point point, Rectangle bounds)
        {
            var dx = point.X < bounds.Left
                ? bounds.Left - point.X
                : point.X > bounds.Right
                    ? point.X - bounds.Right
                    : 0;
            var dy = point.Y < bounds.Top
                ? bounds.Top - point.Y
                : point.Y > bounds.Bottom
                    ? point.Y - bounds.Bottom
                    : 0;
            return (dx * dx) + (dy * dy);
        }
    }
}
