using System.Drawing;
using System.Reflection;
using Pop.Core.Interfaces;
using Pop.Core.Models;
using Pop.Core.Services;

namespace Pop.Tests;

public sealed class MouseHookDragTrackerTests
{
    [Fact]
    public void DragSession_MonitorInfo_FollowsTheCurrentScreenDuringDrag()
    {
        var startMonitor = new MonitorInfo(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
        var secondMonitor = new MonitorInfo(new Rectangle(1920, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1040));
        var startPoint = new Point(100, 100);
        var movedPoint = new Point(2200, 120);
        var tracker = new MouseHookDragTracker(new FakeWindowInspector(startPoint, movedPoint, startMonitor, secondMonitor));

        DragSession? startedSession = null;
        DragSession? completedSession = null;
        DragSession? updatedSession = null;

        tracker.DragStarted += (_, e) => startedSession = e.Session;
        tracker.DragUpdated += (_, e) => updatedSession = e.Session;
        tracker.DragCompleted += (_, e) => completedSession = e.Session;

        var origin = DateTimeOffset.UtcNow;

        InvokePrivate(tracker, "HandleLeftButtonDown", startPoint, origin);
        Assert.NotNull(startedSession);
        Assert.Equal(startMonitor, startedSession!.MonitorInfo);

        InvokePrivate(tracker, "HandleMouseMove", movedPoint, origin.AddMilliseconds(40));
        Assert.NotNull(updatedSession);
        Assert.Same(startedSession, updatedSession);
        Assert.Equal(secondMonitor, updatedSession!.MonitorInfo);

        InvokePrivate(tracker, "HandleLeftButtonUp", movedPoint, origin.AddMilliseconds(80));
        Assert.NotNull(completedSession);
        Assert.Same(startedSession, completedSession);
        Assert.Equal(secondMonitor, completedSession!.MonitorInfo);
    }

    private static void InvokePrivate(object target, string methodName, Point point, DateTimeOffset timestamp)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new InvalidOperationException($"Unable to find method {methodName}.");
        }

        method.Invoke(target, new object[] { point, timestamp });
    }

    private sealed class FakeWindowInspector : IWindowInspector
    {
        private readonly Dictionary<Point, WindowInspectionResult> _windowInspections = new();
        private readonly Dictionary<Point, MonitorInfo> _monitorInspections = new();

        public FakeWindowInspector(Point startPoint, Point movedPoint, MonitorInfo startMonitor, MonitorInfo movedMonitor)
        {
            _windowInspections[startPoint] = CreateSupportedInspection(startMonitor);
            _monitorInspections[startPoint] = startMonitor;
            _monitorInspections[movedPoint] = movedMonitor;
        }

        public WindowInspectionResult InspectWindowAt(Point screenPoint)
        {
            return _windowInspections.TryGetValue(screenPoint, out var inspection)
                ? inspection
                : CreateUnsupportedInspection();
        }

        public MonitorInfo InspectMonitorAt(Point screenPoint)
        {
            return _monitorInspections.TryGetValue(screenPoint, out var monitorInfo)
                ? monitorInfo
                : MonitorInfo.Empty;
        }

        private static WindowInspectionResult CreateSupportedInspection(MonitorInfo monitorInfo)
        {
            var traits = new WindowTraits(true, true, true, false, false, true, false, false, false, false);
            return new WindowInspectionResult(
                new IntPtr(1),
                new Rectangle(100, 100, 800, 600),
                monitorInfo,
                traits,
                WindowEligibilityResult.Supported());
        }

        private static WindowInspectionResult CreateUnsupportedInspection()
        {
            var traits = new WindowTraits(false, false, false, false, false, false, false, false, false, false);
            return new WindowInspectionResult(
                IntPtr.Zero,
                Rectangle.Empty,
                MonitorInfo.Empty,
                traits,
                WindowEligibilityResult.Unsupported(WindowEligibilityReason.Unknown));
        }
    }
}
