using System.Drawing;
using System.Runtime.InteropServices;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.App.Mac;

public static class MacBridgeRuntime
{
    public static PopSnapDecisionDto EvaluateDragGestureManaged(
        ReadOnlySpan<PopDragSampleDto> samples,
        ReadOnlySpan<PopMonitorInfoDto> monitors,
        PopDragContextDto context,
        PopAppSettingsDto settings)
    {
        var availableMonitors = monitors.Length == 0
            ? [context.CurrentMonitor.ToManaged()]
            : monitors.ToArray().Select(dto => dto.ToManaged()).ToArray();

        var session = new DragSession(IntPtr.Zero, context.InitialMonitor.ToManaged(), context.InitialBounds.ToRectangle());
        session.UpdateCurrentMonitorInfo(context.CurrentMonitor.ToManaged());
        session.UpdateCurrentBounds(context.CurrentBounds.ToRectangle());

        DragSample? releaseSample = null;
        foreach (var sample in samples)
        {
            var managedSample = sample.ToManaged();
            session.AddSample(managedSample);
            releaseSample = managedSample;
        }

        if (releaseSample.HasValue)
        {
            session.CompleteRelease(releaseSample.Value, context.IsOptionPressedAtRelease != 0);
        }

        var decider = new SnapDecider(point => LookupMonitor(point, availableMonitors, context.CurrentMonitor.ToManaged()));
        var decision = decider.Decide(session, settings.ToManaged());
        return decision.ToDto();
    }

    public static PopRectDto GetTileBoundsManaged(int target, PopMonitorInfoDto monitor)
    {
        var snapTarget = Enum.IsDefined(typeof(SnapTarget), target)
            ? (SnapTarget)target
            : SnapTarget.None;

        return TileLayoutCalculator.GetTileBounds(snapTarget, monitor.ToManaged()).ToDto();
    }

    public static PopAnimationPlanDto CreateAnimationPlanManaged(
        PopRectDto startBounds,
        PopRectDto targetBounds,
        double releaseVelocityX,
        int durationMs)
    {
        var animator = new WindowAnimator();
        var plan = animator.CreatePlan(startBounds.ToRectangle(), targetBounds.ToRectangle(), releaseVelocityX, durationMs);
        var frames = plan.Frames.Select(frame => new PopAnimationFrameDto((int)frame.Offset.TotalMilliseconds, frame.Bounds.ToDto())).ToArray();
        var frameBuffer = CopyFramesToUnmanagedMemory(frames);

        return new PopAnimationPlanDto(
            frameBuffer,
            frames.Length,
            plan.FinalBounds.ToDto(),
            plan.DurationMs,
            plan.MaxOvershootPx);
    }

    public static string FormatDiagnosticEventManaged(
        long timestampUnixMilliseconds,
        string category,
        string message,
        IReadOnlyDictionary<string, string?>? fields)
    {
        var diagnosticEvent = new DiagnosticEvent(
            DateTimeOffset.FromUnixTimeMilliseconds(timestampUnixMilliseconds),
            category,
            message,
            fields);

        return DiagnosticsLogFormatter.Format(diagnosticEvent);
    }

    public static void FreeAnimationPlanManaged(PopAnimationPlanDto plan)
    {
        if (plan.Frames != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(plan.Frames);
        }
    }

    public static void FreeUtf8StringManaged(IntPtr pointer)
    {
        if (pointer != IntPtr.Zero)
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }

    private static unsafe IntPtr CopyFramesToUnmanagedMemory(PopAnimationFrameDto[] frames)
    {
        if (frames.Length == 0)
        {
            return IntPtr.Zero;
        }

        var bytes = sizeof(PopAnimationFrameDto) * frames.Length;
        var pointer = Marshal.AllocCoTaskMem(bytes);
        var destination = new Span<PopAnimationFrameDto>(pointer.ToPointer(), frames.Length);
        frames.CopyTo(destination);
        return pointer;
    }

    private static MonitorInfo LookupMonitor(Point point, IReadOnlyList<MonitorInfo> monitors, MonitorInfo fallbackMonitor)
    {
        foreach (var monitor in monitors)
        {
            if (Contains(monitor.Bounds, point))
            {
                return monitor;
            }
        }

        return fallbackMonitor;
    }

    private static bool Contains(Rectangle rectangle, Point point)
    {
        return rectangle != Rectangle.Empty &&
               point.X >= rectangle.Left &&
               point.X < rectangle.Right &&
               point.Y >= rectangle.Top &&
               point.Y < rectangle.Bottom;
    }
}

public static class MacBridgeDtoConversions
{
    public static AppSettings ToManaged(this PopAppSettingsDto dto)
    {
        return new AppSettings
        {
            Enabled = dto.Enabled != 0,
            LaunchAtStartup = dto.LaunchAtStartup != 0,
            ThrowVelocityThresholdPxPerSec = dto.ThrowVelocityThresholdPxPerSec,
            HorizontalDominanceRatio = dto.HorizontalDominanceRatio,
            GlideDurationMs = dto.GlideDurationMs,
            EnableDiagnostics = dto.EnableDiagnostics != 0
        };
    }

    public static DragSample ToManaged(this PopDragSampleDto dto)
    {
        return new DragSample(new Point(dto.X, dto.Y), DateTimeOffset.FromUnixTimeMilliseconds(dto.TimestampUnixMilliseconds));
    }

    public static Rectangle ToRectangle(this PopRectDto dto) => new(dto.X, dto.Y, dto.Width, dto.Height);

    public static PopRectDto ToDto(this Rectangle rectangle) => new(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);

    public static MonitorInfo ToManaged(this PopMonitorInfoDto dto) => new(dto.Bounds.ToRectangle(), dto.WorkArea.ToRectangle());

    public static PopMonitorInfoDto ToDto(this MonitorInfo monitorInfo) => new(monitorInfo.Bounds.ToDto(), monitorInfo.WorkArea.ToDto());

    public static PopPointDto ToDto(this Point point) => new(point.X, point.Y);

    public static PopSnapDecisionDto ToDto(this SnapDecision decision)
    {
        return new PopSnapDecisionDto(
            (int)decision.Target,
            decision.TargetMonitorInfo.ToDto(),
            decision.ProjectedLandingPoint.ToDto(),
            decision.HorizontalVelocityPxPerSec,
            decision.VerticalVelocityPxPerSec,
            decision.HorizontalDominanceRatio,
            decision.IsQualified ? (byte)1 : (byte)0,
            (int)decision.RejectionReason);
    }
}
