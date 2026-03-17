using System.Drawing;
using Pop.Core.Interfaces;
using Pop.Core.Interop;
using Pop.Core.Models;

namespace Pop.Core.Services;

public sealed class WindowInspector(WindowEligibilityEvaluator evaluator) : IWindowInspector
{
    private readonly WindowEligibilityEvaluator _evaluator = evaluator;

    public WindowInspectionResult InspectWindowAt(Point screenPoint)
    {
        var rawHandle = NativeMethods.WindowFromPoint(new NativeMethods.PointStruct(screenPoint.X, screenPoint.Y));
        if (rawHandle == IntPtr.Zero)
        {
            return CreateUnsupportedResult();
        }

        var windowHandle = NativeMethods.GetAncestor(rawHandle, NativeMethods.GaRoot);
        if (windowHandle == IntPtr.Zero)
        {
            windowHandle = rawHandle;
        }

        var bounds = TryGetWindowBounds(windowHandle);
        var monitorInfo = TryGetMonitorInfo(windowHandle);
        var traits = BuildTraits(windowHandle, screenPoint, bounds, monitorInfo);
        var eligibility = _evaluator.Evaluate(traits);

        return new WindowInspectionResult(windowHandle, bounds, monitorInfo, traits, eligibility);
    }

    public MonitorInfo InspectMonitorAt(Point screenPoint)
    {
        return TryGetMonitorInfo(screenPoint);
    }

    public WindowStateSnapshot InspectWindowState(IntPtr windowHandle)
    {
        return new WindowStateSnapshot(TryGetWindowBounds(windowHandle), TryGetMonitorInfo(windowHandle));
    }

    private static WindowInspectionResult CreateUnsupportedResult()
    {
        var traits = new WindowTraits(false, false, false, false, false, false, false, false, false, false);
        return new WindowInspectionResult(IntPtr.Zero, Rectangle.Empty, MonitorInfo.Empty, traits, WindowEligibilityResult.Unsupported(WindowEligibilityReason.Unknown, "No eligible window was found under the pointer."));
    }

    private static Rectangle TryGetWindowBounds(IntPtr windowHandle)
    {
        return NativeMethods.GetWindowRect(windowHandle, out var rect) ? rect.ToRectangle() : Rectangle.Empty;
    }

    private static MonitorInfo TryGetMonitorInfo(IntPtr windowHandle)
    {
        var monitorHandle = NativeMethods.MonitorFromWindow(windowHandle, NativeMethods.MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return MonitorInfo.Empty;
        }

        var monitorInfo = new NativeMethods.MonitorInfoEx
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };

        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return MonitorInfo.Empty;
        }

        return new MonitorInfo(monitorInfo.RcMonitor.ToRectangle(), monitorInfo.RcWork.ToRectangle());
    }

    private static MonitorInfo TryGetMonitorInfo(Point screenPoint)
    {
        var monitorHandle = NativeMethods.MonitorFromPoint(new NativeMethods.PointStruct(screenPoint.X, screenPoint.Y), NativeMethods.MonitorDefaultToNearest);
        if (monitorHandle == IntPtr.Zero)
        {
            return MonitorInfo.Empty;
        }

        var monitorInfo = new NativeMethods.MonitorInfoEx
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };

        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo))
        {
            return MonitorInfo.Empty;
        }

        return new MonitorInfo(monitorInfo.RcMonitor.ToRectangle(), monitorInfo.RcWork.ToRectangle());
    }

    private static WindowTraits BuildTraits(IntPtr windowHandle, Point screenPoint, Rectangle bounds, MonitorInfo monitorInfo)
    {
        var style = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlStyle).ToInt64();
        var exStyle = NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle).ToInt64();
        var hasOwner = NativeMethods.GetWindow(windowHandle, NativeMethods.GwOwner) != IntPtr.Zero;
        var processId = GetProcessId(windowHandle);
        var isAppWindow = (exStyle & NativeMethods.WsExAppWindow) != 0;
        var isStandardTopLevelWindow = (style & NativeMethods.WsChild) == 0 &&
                                       (exStyle & NativeMethods.WsExToolWindow) == 0 &&
                                       (!hasOwner || isAppWindow);

        return new WindowTraits(
            IsCaptionHit(windowHandle, screenPoint),
            NativeMethods.IsWindowVisible(windowHandle),
            (style & NativeMethods.WsThickFrame) != 0,
            NativeMethods.IsIconic(windowHandle),
            NativeMethods.IsZoomed(windowHandle),
            isStandardTopLevelWindow,
            IsFullscreen(bounds, monitorInfo.Bounds),
            IsElevatedProcess(processId),
            IsCloaked(windowHandle),
            processId == Environment.ProcessId);
    }

    private static bool IsCaptionHit(IntPtr windowHandle, Point screenPoint)
    {
        var result = NativeMethods.SendMessage(windowHandle, NativeMethods.WmNcHitTest, IntPtr.Zero, NativeMethods.PackScreenPoint(screenPoint));
        return result.ToInt32() == NativeMethods.HtCaption;
    }

    private static uint GetProcessId(IntPtr windowHandle)
    {
        NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        return processId;
    }

    private static bool IsFullscreen(Rectangle windowBounds, Rectangle monitorBounds)
    {
        if (windowBounds == Rectangle.Empty || monitorBounds == Rectangle.Empty)
        {
            return false;
        }

        return Math.Abs(windowBounds.Left - monitorBounds.Left) <= 1 &&
               Math.Abs(windowBounds.Top - monitorBounds.Top) <= 1 &&
               Math.Abs(windowBounds.Right - monitorBounds.Right) <= 1 &&
               Math.Abs(windowBounds.Bottom - monitorBounds.Bottom) <= 1;
    }

    private static bool IsCloaked(IntPtr windowHandle)
    {
        try
        {
            return NativeMethods.DwmGetWindowAttribute(windowHandle, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int)) == 0 &&
                   cloaked != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsElevatedProcess(uint processId)
    {
        if (processId == 0)
        {
            return true;
        }

        var processHandle = NativeMethods.OpenProcess(NativeMethods.ProcessQueryLimitedInformation, false, processId);
        if (processHandle == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            if (!NativeMethods.OpenProcessToken(processHandle, NativeMethods.TokenQuery, out var tokenHandle))
            {
                return true;
            }

            try
            {
                const int tokenElevationClass = 20;
                if (!NativeMethods.GetTokenInformation(tokenHandle, tokenElevationClass, out NativeMethods.TokenElevation elevation, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.TokenElevation>(), out _))
                {
                    return true;
                }

                return elevation.TokenIsElevated != 0;
            }
            finally
            {
                NativeMethods.CloseHandle(tokenHandle);
            }
        }
        finally
        {
            NativeMethods.CloseHandle(processHandle);
        }
    }
}
