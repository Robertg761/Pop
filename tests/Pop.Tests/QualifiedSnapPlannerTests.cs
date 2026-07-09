using System.Drawing;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class QualifiedSnapPlannerTests
{
    private static readonly MonitorInfo MainMonitor = new(
        new Rectangle(0, 0, 1920, 1080),
        new Rectangle(0, 0, 1920, 1040));

    private static readonly AppSettings TestSettings = new()
    {
        ThrowVelocityThresholdPxPerSec = 1500,
        HorizontalDominanceRatio = 1.75,
        GlideDurationMs = 200
    };

    [Fact]
    public void TryCreatePlan_ReturnsFalse_WhenDecisionIsNotQualified()
    {
        var planner = CreatePlanner();
        var session = CreateSession((0, 20, 0), (40, 30, 300));
        var decision = planner.Decide(session, TestSettings);

        var created = planner.TryCreatePlan(
            session,
            decision,
            TestSettings,
            static (_, tile) => tile,
            out _);

        Assert.False(decision.IsQualified);
        Assert.False(created);
    }

    [Fact]
    public void TryCreatePlan_BuildsLeftHalfAnimation_ForFastLeftThrow()
    {
        var planner = CreatePlanner();
        var session = CreateSession((400, 80, 0), (120, 95, 100));
        var decision = planner.Decide(session, TestSettings);

        var created = planner.TryCreatePlan(
            session,
            decision,
            TestSettings,
            static (_, tile) => tile,
            out var plan);

        Assert.True(created);
        Assert.Equal(SnapTarget.LeftHalf, plan.Decision.Target);
        Assert.Equal(MainMonitor, plan.ActiveMonitor);
        Assert.Equal(new Rectangle(0, 0, 960, 1040), plan.VisibleTileBounds);
        Assert.Equal(plan.VisibleTileBounds, plan.SnapBounds);
        Assert.Equal(plan.SnapBounds, plan.AnimationPlan.FinalBounds);
        Assert.NotEmpty(plan.AnimationPlan.Frames);
    }

    [Fact]
    public void TryCreatePlan_UsesSnapBoundsCalculatorResult()
    {
        var planner = CreatePlanner();
        var session = CreateSession((0, 50, 0), (220, 65, 100));
        var decision = planner.Decide(session, TestSettings);
        var adjusted = new Rectangle(10, 20, 900, 900);

        var created = planner.TryCreatePlan(
            session,
            decision,
            TestSettings,
            (_, _) => adjusted,
            out var plan);

        Assert.True(created);
        Assert.Equal(adjusted, plan.SnapBounds);
        Assert.Equal(adjusted, plan.AnimationPlan.FinalBounds);
    }

    private static QualifiedSnapPlanner CreatePlanner()
        => new(new SnapDecider(_ => MainMonitor));

    private static DragSession CreateSession(params (int x, int y, int ms)[] samples)
    {
        var session = new DragSession(new IntPtr(42), MainMonitor, new Rectangle(100, 100, 800, 600));
        var origin = DateTimeOffset.UtcNow;
        foreach (var sample in samples)
        {
            session.AddSample(new DragSample(new Point(sample.x, sample.y), origin.AddMilliseconds(sample.ms)));
        }

        session.UpdateCurrentBounds(new Rectangle(100, 100, 800, 600));
        session.UpdateCurrentMonitorInfo(MainMonitor);
        if (samples.Length > 0)
        {
            var last = samples[^1];
            session.CompleteRelease(
                new DragSample(new Point(last.x, last.y), origin.AddMilliseconds(last.ms)),
                isCtrlPressedAtRelease: false);
        }

        return session;
    }
}
