using System.Drawing;
using System.Reflection;
using Pop.App.Windows.Platform.Input;
using Pop.Core.Models;
using Pop.Platform.Abstractions.Windowing;

namespace Pop.Tests;

public sealed class MouseHookDragTrackerTests
{
    [Fact]
    public void DragSession_CurrentWindowState_FollowsTheWindowDuringDrag()
    {
        var startMonitor = new MonitorInfo(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
        var secondMonitor = new MonitorInfo(new Rectangle(1920, 0, 1920, 1080), new Rectangle(1920, 0, 1920, 1040));
        var startPoint = new Point(100, 100);
        var movedPoint = new Point(2200, 120);
        var startBounds = new Rectangle(100, 100, 800, 600);
        var movedBounds = new Rectangle(2020, 140, 800, 600);
        var tracker = new MouseHookDragTracker(new FakeWindowInspector(startPoint, startMonitor, secondMonitor, startBounds, movedBounds));

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
        Assert.Equal(startMonitor, startedSession.CurrentMonitorInfo);
        Assert.Equal(startBounds, startedSession.CurrentBounds);

        InvokePrivate(tracker, "HandleMouseMove", movedPoint, origin.AddMilliseconds(40));
        Assert.NotNull(updatedSession);
        Assert.Same(startedSession, updatedSession);
        Assert.Equal(startMonitor, updatedSession!.MonitorInfo);
        Assert.Equal(secondMonitor, updatedSession.CurrentMonitorInfo);
        Assert.Equal(movedBounds, updatedSession.CurrentBounds);

        InvokePrivate(tracker, "HandleLeftButtonUp", movedPoint, origin.AddMilliseconds(80));
        Assert.NotNull(completedSession);
        Assert.Same(startedSession, completedSession);
        Assert.Equal(startMonitor, completedSession!.MonitorInfo);
        Assert.Equal(secondMonitor, completedSession.CurrentMonitorInfo);
        Assert.Equal(movedBounds, completedSession.CurrentBounds);
    }

    [Fact]
    public void DragSession_CapturesCtrlState_WhenReleased()
    {
        var startMonitor = new MonitorInfo(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
        var startPoint = new Point(100, 100);
        var endPoint = new Point(400, 120);
        var startBounds = new Rectangle(100, 100, 800, 600);
        var endBounds = new Rectangle(300, 120, 800, 600);
        var tracker = new MouseHookDragTracker(
            new FakeWindowInspector(startPoint, startMonitor, startMonitor, startBounds, endBounds),
            () => true);

        DragSession? completedSession = null;
        tracker.DragCompleted += (_, e) => completedSession = e.Session;

        var origin = DateTimeOffset.UtcNow;
        InvokePrivate(tracker, "HandleLeftButtonDown", startPoint, origin);
        InvokePrivate(tracker, "HandleLeftButtonUp", endPoint, origin.AddMilliseconds(60));

        Assert.NotNull(completedSession);
        Assert.True(completedSession!.IsCtrlPressedAtRelease);
        Assert.Equal(endPoint, completedSession.ReleaseSample?.Position);
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
        private readonly Queue<WindowStateSnapshot> _windowStates = new();
        private readonly MonitorInfo _startMonitor;

        public FakeWindowInspector(Point startPoint, MonitorInfo startMonitor, MonitorInfo movedMonitor, Rectangle startBounds, Rectangle movedBounds)
        {
            _startMonitor = startMonitor;
            _windowInspections[startPoint] = CreateSupportedInspection(startMonitor);
            _windowStates.Enqueue(new WindowStateSnapshot(startBounds, startMonitor));
            _windowStates.Enqueue(new WindowStateSnapshot(movedBounds, movedMonitor));
            _windowStates.Enqueue(new WindowStateSnapshot(movedBounds, movedMonitor));
        }

        public WindowInspectionResult InspectWindowAt(Point screenPoint)
        {
            return _windowInspections.TryGetValue(screenPoint, out var inspection)
                ? inspection
                : CreateUnsupportedInspection();
        }

        public MonitorInfo InspectMonitorAt(Point screenPoint)
        {
            return _startMonitor;
        }

        public WindowStateSnapshot InspectWindowState(IntPtr windowHandle)
        {
            if (_windowStates.Count == 0)
            {
                return new WindowStateSnapshot(Rectangle.Empty, MonitorInfo.Empty);
            }

            return _windowStates.Dequeue();
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
