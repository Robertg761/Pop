using System.Runtime.InteropServices;

namespace Pop.App.Mac;

public static unsafe class MacBridgeExports
{
    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_EvaluateDragGesture")]
    public static PopSnapDecisionDto EvaluateDragGesture(
        PopDragSampleDto* samples,
        int sampleCount,
        PopMonitorInfoDto* monitors,
        int monitorCount,
        PopDragContextDto context,
        PopAppSettingsDto settings)
    {
        try
        {
            var sampleSpan = samples is null || sampleCount <= 0
                ? ReadOnlySpan<PopDragSampleDto>.Empty
                : new ReadOnlySpan<PopDragSampleDto>(samples, sampleCount);
            var monitorSpan = monitors is null || monitorCount <= 0
                ? ReadOnlySpan<PopMonitorInfoDto>.Empty
                : new ReadOnlySpan<PopMonitorInfoDto>(monitors, monitorCount);

            return MacBridgeRuntime.EvaluateDragGestureManaged(sampleSpan, monitorSpan, context, settings);
        }
        catch
        {
            // A managed exception escaping an [UnmanagedCallersOnly] export fail-fasts the whole
            // menu-bar process under NativeAOT. Return an unqualified decision instead so a
            // malformed sample can never crash a drag.
            return default;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_GetTileBounds")]
    public static PopRectDto GetTileBounds(int target, PopMonitorInfoDto monitor)
    {
        try
        {
            return MacBridgeRuntime.GetTileBoundsManaged(target, monitor);
        }
        catch
        {
            return default;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_CreateAnimationPlan")]
    public static PopAnimationPlanDto CreateAnimationPlan(
        PopRectDto startBounds,
        PopRectDto targetBounds,
        double releaseVelocityX,
        int durationMs)
    {
        try
        {
            return MacBridgeRuntime.CreateAnimationPlanManaged(startBounds, targetBounds, releaseVelocityX, durationMs);
        }
        catch
        {
            // Fall back to a frameless plan that snaps straight to the target, rather than
            // fail-fasting the process.
            return new PopAnimationPlanDto(IntPtr.Zero, 0, targetBounds, Math.Max(0, durationMs), 0);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_FreeAnimationPlan")]
    public static void FreeAnimationPlan(PopAnimationPlanDto plan)
    {
        try
        {
            MacBridgeRuntime.FreeAnimationPlanManaged(plan);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_CreateRestoreBounds")]
    public static PopRestoreBoundsDto CreateRestoreBounds(
        PopRectDto currentBounds,
        PopRectDto snappedBounds,
        PopRectDto previousBounds,
        PopPointDto dragPoint,
        PopRectDto workArea)
    {
        try
        {
            return MacBridgeRuntime.CreateRestoreBoundsManaged(currentBounds, snappedBounds, previousBounds, dragPoint, workArea);
        }
        catch
        {
            return default;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_FormatDiagnosticEvent")]
    public static IntPtr FormatDiagnosticEvent(
        long timestampUnixMilliseconds,
        IntPtr category,
        IntPtr message,
        PopDiagnosticFieldDto* fields,
        int fieldCount)
    {
        try
        {
            var managedFields = new Dictionary<string, string?>();
            if (fields is not null && fieldCount > 0)
            {
                var fieldSpan = new ReadOnlySpan<PopDiagnosticFieldDto>(fields, fieldCount);
                foreach (var field in fieldSpan)
                {
                    var key = Marshal.PtrToStringUTF8(field.Key);
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    managedFields[key] = field.Value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(field.Value);
                }
            }

            var json = MacBridgeRuntime.FormatDiagnosticEventManaged(
                timestampUnixMilliseconds,
                Marshal.PtrToStringUTF8(category) ?? string.Empty,
                Marshal.PtrToStringUTF8(message) ?? string.Empty,
                managedFields);

            return Marshal.StringToCoTaskMemUTF8(json);
        }
        catch
        {
            // The Swift caller treats a null pointer as an empty diagnostic line.
            return IntPtr.Zero;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_FreeUtf8String")]
    public static void FreeUtf8String(IntPtr pointer)
    {
        MacBridgeRuntime.FreeUtf8StringManaged(pointer);
    }
}
