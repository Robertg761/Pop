using System.Drawing;
using Pop.Core.Models;

namespace Pop.Tests;

public sealed class DragSessionTests
{
    [Fact]
    public void GetCurrentBoundsEstimate_UsesOriginalSampleAfterSampleTrimming()
    {
        var monitor = new MonitorInfo(new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040));
        var session = new DragSession(new IntPtr(1), monitor, new Rectangle(100, 100, 800, 600));
        var origin = DateTimeOffset.UtcNow;

        for (var index = 0; index < 60; index++)
        {
            session.AddSample(new DragSample(new Point(index * 10, index), origin.AddMilliseconds(index * 8)));
        }

        Assert.Equal(48, session.Samples.Count);
        Assert.Equal(new Rectangle(690, 159, 800, 600), session.GetCurrentBoundsEstimate());
    }
}
