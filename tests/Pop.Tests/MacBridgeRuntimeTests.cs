using System.Drawing;
using Pop.App.Mac;
using Pop.Core.Models;

namespace Pop.Tests;

public sealed class MacBridgeRuntimeTests
{
    private static readonly PopMonitorInfoDto MainMonitor = new(
        new PopRectDto(0, 0, 1920, 1080),
        new PopRectDto(0, 0, 1920, 1040));

    private static readonly PopMonitorInfoDto RightMonitor = new(
        new PopRectDto(1920, 0, 1920, 1080),
        new PopRectDto(1920, 0, 1920, 1040));

    private static readonly PopMonitorInfoDto GappedRightMonitor = new(
        new PopRectDto(2500, 0, 1920, 1080),
        new PopRectDto(2500, 0, 1920, 1040));

    private static readonly PopMonitorInfoDto LowerMonitor = new(
        new PopRectDto(0, 1080, 1920, 1080),
        new PopRectDto(0, 1080, 1920, 1040));

    private static readonly PopAppSettingsDto Settings = new(
        enabled: 1,
        launchAtStartup: 0,
        throwVelocityThresholdPxPerSec: 1500,
        horizontalDominanceRatio: 1.75,
        glideDurationMs: 220,
        enableDiagnostics: 1);

    [Fact]
    public void EvaluateDragGestureManaged_QualifiesOptionCrossMonitorThrow()
    {
        var origin = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new PopDragSampleDto(1600, 200, origin.ToUnixTimeMilliseconds()),
            new PopDragSampleDto(2100, 210, origin.AddMilliseconds(50).ToUnixTimeMilliseconds()),
            new PopDragSampleDto(2500, 220, origin.AddMilliseconds(100).ToUnixTimeMilliseconds())
        };
        var monitors = new[] { MainMonitor, RightMonitor };
        var context = new PopDragContextDto(
            MainMonitor,
            MainMonitor,
            new PopRectDto(1450, 100, 900, 700),
            new PopRectDto(2050, 100, 900, 700),
            isOptionPressedAtRelease: 1);

        var decision = MacBridgeRuntime.EvaluateDragGestureManaged(samples, monitors, context, Settings);

        Assert.Equal((int)SnapTarget.RightHalf, decision.Target);
        Assert.Equal(1, decision.IsQualified);
        Assert.Equal(RightMonitor.Bounds.X, decision.TargetMonitor.Bounds.X);
    }

    [Fact]
    public void EvaluateDragGestureManaged_UsesNearestMonitor_WhenProjectedLandingOvershootsAcrossAGap()
    {
        var origin = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new PopDragSampleDto(400, 200, origin.ToUnixTimeMilliseconds()),
            new PopDragSampleDto(1000, 210, origin.AddMilliseconds(50).ToUnixTimeMilliseconds()),
            new PopDragSampleDto(1600, 220, origin.AddMilliseconds(100).ToUnixTimeMilliseconds())
        };
        var monitors = new[] { MainMonitor, GappedRightMonitor };
        var context = new PopDragContextDto(
            MainMonitor,
            MainMonitor,
            new PopRectDto(700, 100, 900, 700),
            new PopRectDto(1150, 100, 900, 700),
            isOptionPressedAtRelease: 1);

        var decision = MacBridgeRuntime.EvaluateDragGestureManaged(samples, monitors, context, Settings);

        Assert.Equal(1, decision.IsQualified);
        Assert.Equal(GappedRightMonitor.Bounds.X, decision.TargetMonitor.Bounds.X);
        Assert.Equal((int)SnapTarget.RightHalf, decision.Target);
    }

    [Fact]
    public void EvaluateDragGestureManaged_PrefersHorizontalMonitor_WhenLowerMonitorIsCloserToGapLandingPoint()
    {
        var origin = DateTimeOffset.UtcNow;
        var samples = new[]
        {
            new PopDragSampleDto(400, 760, origin.ToUnixTimeMilliseconds()),
            new PopDragSampleDto(1000, 800, origin.AddMilliseconds(50).ToUnixTimeMilliseconds()),
            new PopDragSampleDto(1600, 840, origin.AddMilliseconds(100).ToUnixTimeMilliseconds())
        };
        var monitors = new[] { MainMonitor, GappedRightMonitor, LowerMonitor };
        var context = new PopDragContextDto(
            MainMonitor,
            MainMonitor,
            new PopRectDto(700, 620, 900, 700),
            new PopRectDto(1150, 640, 900, 700),
            isOptionPressedAtRelease: 1);

        var decision = MacBridgeRuntime.EvaluateDragGestureManaged(samples, monitors, context, Settings);

        Assert.Equal(1, decision.IsQualified);
        Assert.Equal(GappedRightMonitor.Bounds.X, decision.TargetMonitor.Bounds.X);
        Assert.Equal((int)SnapTarget.RightHalf, decision.Target);
    }

    [Fact]
    public void GetTileBoundsManaged_SplitsOddMonitorWidth()
    {
        var monitor = new PopMonitorInfoDto(
            new PopRectDto(0, 0, 1921, 1080),
            new PopRectDto(0, 0, 1921, 1040));

        var left = MacBridgeRuntime.GetTileBoundsManaged((int)SnapTarget.LeftHalf, monitor);
        var right = MacBridgeRuntime.GetTileBoundsManaged((int)SnapTarget.RightHalf, monitor);

        Assert.Equal(new PopRectDto(0, 0, 960, 1040), left);
        Assert.Equal(new PopRectDto(960, 0, 961, 1040), right);
    }

    [Fact]
    public unsafe void CreateAnimationPlanManaged_AllocatesExpectedFrameBuffer()
    {
        var plan = MacBridgeRuntime.CreateAnimationPlanManaged(
            new PopRectDto(400, 160, 900, 620),
            new PopRectDto(0, 0, 960, 1040),
            releaseVelocityX: 2400,
            durationMs: 240);

        try
        {
            Assert.True(plan.FrameCount > 0);
            Assert.NotEqual(IntPtr.Zero, plan.Frames);
            Assert.Equal(new PopRectDto(0, 0, 960, 1040), plan.FinalBounds);

            var frames = new ReadOnlySpan<PopAnimationFrameDto>(plan.Frames.ToPointer(), plan.FrameCount);
            Assert.Equal(plan.FinalBounds, frames[^1].Bounds);
            Assert.True(plan.MaxOvershootPx > 0);
        }
        finally
        {
            MacBridgeRuntime.FreeAnimationPlanManaged(plan);
        }
    }

    [Fact]
    public void FormatDiagnosticEventManaged_UsesSharedFormatterContract()
    {
        var json = MacBridgeRuntime.FormatDiagnosticEventManaged(
            DateTimeOffset.UnixEpoch.AddSeconds(1).ToUnixTimeMilliseconds(),
            "drag-release",
            "Snap qualified and animation plan generated.",
            new Dictionary<string, string?>
            {
                ["target"] = "RightHalf",
                ["frames"] = "27"
            });

        Assert.Contains("\"category\":\"drag-release\"", json);
        Assert.Contains("\"target\":\"RightHalf\"", json);
        Assert.Contains("\"frames\":\"27\"", json);
    }

    [Fact]
    public void DtoValues_ConvertIntoStableManagedShapes()
    {
        var rectangle = new PopRectDto(10, 20, 30, 40).ToRectangle();
        var monitor = new PopMonitorInfoDto(new PopRectDto(1, 2, 3, 4), new PopRectDto(5, 6, 7, 8)).ToManaged();
        var settings = Settings.ToManaged();

        Assert.Equal(new Rectangle(10, 20, 30, 40), rectangle);
        Assert.Equal(new Rectangle(1, 2, 3, 4), monitor.Bounds);
        Assert.True(settings.Enabled);
        Assert.Equal(220, settings.GlideDurationMs);
    }
}
