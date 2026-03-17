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
        InitialBounds = initialBounds;
    }

    public IntPtr WindowHandle { get; }

    public MonitorInfo MonitorInfo { get; }

    public Rectangle InitialBounds { get; }

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
}
