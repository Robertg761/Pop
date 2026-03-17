using System.Collections.ObjectModel;
using System.Drawing;

namespace Pop.Core.Models;

public sealed class DragSession
{
    private readonly List<DragSample> _samples = [];

    public DragSession(IntPtr windowHandle, MonitorInfo monitorInfo, Rectangle initialBounds)
    {
        WindowHandle = windowHandle;
        MonitorInfo = monitorInfo;
        CurrentMonitorInfo = monitorInfo;
        InitialBounds = initialBounds;
        CurrentBounds = initialBounds;
    }

    public IntPtr WindowHandle { get; }

    public MonitorInfo MonitorInfo { get; }

    public MonitorInfo CurrentMonitorInfo { get; private set; }

    public Rectangle InitialBounds { get; }

    public Rectangle CurrentBounds { get; private set; }

    public SnapTarget CurrentPredictedTarget { get; set; }

    public ReadOnlyCollection<DragSample> Samples => _samples.AsReadOnly();

    public void AddSample(DragSample sample)
    {
        _samples.Add(sample);

        if (_samples.Count > 48)
        {
            _samples.RemoveRange(0, _samples.Count - 48);
        }
    }

    public Rectangle GetCurrentBoundsEstimate()
    {
        if (_samples.Count < 2)
        {
            return InitialBounds;
        }

        var first = _samples[0].Position;
        var last = _samples[^1].Position;
        var deltaX = last.X - first.X;
        var deltaY = last.Y - first.Y;

        return new Rectangle(
            InitialBounds.X + deltaX,
            InitialBounds.Y + deltaY,
            InitialBounds.Width,
            InitialBounds.Height);
    }

    public void UpdateCurrentMonitorInfo(MonitorInfo monitorInfo)
    {
        CurrentMonitorInfo = monitorInfo;
    }

    public void UpdateCurrentBounds(Rectangle bounds)
    {
        CurrentBounds = bounds;
    }
}
