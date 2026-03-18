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
        var sampleSpan = samples is null || sampleCount <= 0
            ? ReadOnlySpan<PopDragSampleDto>.Empty
            : new ReadOnlySpan<PopDragSampleDto>(samples, sampleCount);
        var monitorSpan = monitors is null || monitorCount <= 0
            ? ReadOnlySpan<PopMonitorInfoDto>.Empty
            : new ReadOnlySpan<PopMonitorInfoDto>(monitors, monitorCount);

        return MacBridgeRuntime.EvaluateDragGestureManaged(sampleSpan, monitorSpan, context, settings);
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_GetTileBounds")]
    public static PopRectDto GetTileBounds(int target, PopMonitorInfoDto monitor)
    {
        return MacBridgeRuntime.GetTileBoundsManaged(target, monitor);
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_CreateAnimationPlan")]
    public static PopAnimationPlanDto CreateAnimationPlan(
        PopRectDto startBounds,
        PopRectDto targetBounds,
        double releaseVelocityX,
        int durationMs)
    {
        return MacBridgeRuntime.CreateAnimationPlanManaged(startBounds, targetBounds, releaseVelocityX, durationMs);
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_FreeAnimationPlan")]
    public static void FreeAnimationPlan(PopAnimationPlanDto plan)
    {
        MacBridgeRuntime.FreeAnimationPlanManaged(plan);
    }

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_FormatDiagnosticEvent")]
    public static IntPtr FormatDiagnosticEvent(
        long timestampUnixMilliseconds,
        IntPtr category,
        IntPtr message,
        PopDiagnosticFieldDto* fields,
        int fieldCount)
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

    [UnmanagedCallersOnly(EntryPoint = "PopMacBridge_FreeUtf8String")]
    public static void FreeUtf8String(IntPtr pointer)
    {
        MacBridgeRuntime.FreeUtf8StringManaged(pointer);
    }
}
