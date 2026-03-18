using System.Runtime.InteropServices;

namespace Pop.App.Mac;

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopPointDto
{
    public PopPointDto(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }

    public int Y { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopRectDto
{
    public PopRectDto(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopMonitorInfoDto
{
    public PopMonitorInfoDto(PopRectDto bounds, PopRectDto workArea)
    {
        Bounds = bounds;
        WorkArea = workArea;
    }

    public PopRectDto Bounds { get; }

    public PopRectDto WorkArea { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopAppSettingsDto
{
    public PopAppSettingsDto(
        byte enabled,
        byte launchAtStartup,
        double throwVelocityThresholdPxPerSec,
        double horizontalDominanceRatio,
        int glideDurationMs,
        byte enableDiagnostics)
    {
        Enabled = enabled;
        LaunchAtStartup = launchAtStartup;
        ThrowVelocityThresholdPxPerSec = throwVelocityThresholdPxPerSec;
        HorizontalDominanceRatio = horizontalDominanceRatio;
        GlideDurationMs = glideDurationMs;
        EnableDiagnostics = enableDiagnostics;
    }

    public byte Enabled { get; }

    public byte LaunchAtStartup { get; }

    public double ThrowVelocityThresholdPxPerSec { get; }

    public double HorizontalDominanceRatio { get; }

    public int GlideDurationMs { get; }

    public byte EnableDiagnostics { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopDragSampleDto
{
    public PopDragSampleDto(int x, int y, long timestampUnixMilliseconds)
    {
        X = x;
        Y = y;
        TimestampUnixMilliseconds = timestampUnixMilliseconds;
    }

    public int X { get; }

    public int Y { get; }

    public long TimestampUnixMilliseconds { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopDragContextDto
{
    public PopDragContextDto(
        PopMonitorInfoDto initialMonitor,
        PopMonitorInfoDto currentMonitor,
        PopRectDto initialBounds,
        PopRectDto currentBounds,
        byte isOptionPressedAtRelease)
    {
        InitialMonitor = initialMonitor;
        CurrentMonitor = currentMonitor;
        InitialBounds = initialBounds;
        CurrentBounds = currentBounds;
        IsOptionPressedAtRelease = isOptionPressedAtRelease;
    }

    public PopMonitorInfoDto InitialMonitor { get; }

    public PopMonitorInfoDto CurrentMonitor { get; }

    public PopRectDto InitialBounds { get; }

    public PopRectDto CurrentBounds { get; }

    public byte IsOptionPressedAtRelease { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopSnapDecisionDto
{
    public PopSnapDecisionDto(
        int target,
        PopMonitorInfoDto targetMonitor,
        PopPointDto projectedLandingPoint,
        double horizontalVelocityPxPerSec,
        double verticalVelocityPxPerSec,
        double horizontalDominanceRatio,
        byte isQualified,
        int rejectionReason)
    {
        Target = target;
        TargetMonitor = targetMonitor;
        ProjectedLandingPoint = projectedLandingPoint;
        HorizontalVelocityPxPerSec = horizontalVelocityPxPerSec;
        VerticalVelocityPxPerSec = verticalVelocityPxPerSec;
        HorizontalDominanceRatio = horizontalDominanceRatio;
        IsQualified = isQualified;
        RejectionReason = rejectionReason;
    }

    public int Target { get; }

    public PopMonitorInfoDto TargetMonitor { get; }

    public PopPointDto ProjectedLandingPoint { get; }

    public double HorizontalVelocityPxPerSec { get; }

    public double VerticalVelocityPxPerSec { get; }

    public double HorizontalDominanceRatio { get; }

    public byte IsQualified { get; }

    public int RejectionReason { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopAnimationFrameDto
{
    public PopAnimationFrameDto(int offsetMilliseconds, PopRectDto bounds)
    {
        OffsetMilliseconds = offsetMilliseconds;
        Bounds = bounds;
    }

    public int OffsetMilliseconds { get; }

    public PopRectDto Bounds { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopAnimationPlanDto
{
    public PopAnimationPlanDto(IntPtr frames, int frameCount, PopRectDto finalBounds, int durationMs, int maxOvershootPx)
    {
        Frames = frames;
        FrameCount = frameCount;
        FinalBounds = finalBounds;
        DurationMs = durationMs;
        MaxOvershootPx = maxOvershootPx;
    }

    public IntPtr Frames { get; }

    public int FrameCount { get; }

    public PopRectDto FinalBounds { get; }

    public int DurationMs { get; }

    public int MaxOvershootPx { get; }
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct PopDiagnosticFieldDto
{
    public PopDiagnosticFieldDto(IntPtr key, IntPtr value)
    {
        Key = key;
        Value = value;
    }

    public IntPtr Key { get; }

    public IntPtr Value { get; }
}
