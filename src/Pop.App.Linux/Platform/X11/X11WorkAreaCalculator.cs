using System.Drawing;

namespace Pop.App.Linux.Platform.X11;

/// <summary>
/// Computes per-monitor work areas from EWMH strut properties. _NET_WORKAREA is a single global
/// rectangle, so intersecting it with each monitor lets one monitor's panel shave space off
/// every monitor; struts describe exactly where each panel reserves space.
/// </summary>
internal static class X11WorkAreaCalculator
{
    /// <summary>
    /// <paramref name="struts"/> holds one entry per panel window: either the 12 values of
    /// _NET_WM_STRUT_PARTIAL (left, right, top, bottom, left_start_y, left_end_y, right_start_y,
    /// right_end_y, top_start_x, top_end_x, bottom_start_x, bottom_end_x) or the 4 values of
    /// _NET_WM_STRUT, all relative to the root window (<paramref name="displayBounds"/>).
    /// Falls back to <paramref name="monitorBounds"/> when the struts would leave no usable area.
    /// </summary>
    public static Rectangle ComputeWorkArea(
        Rectangle monitorBounds,
        Rectangle displayBounds,
        IEnumerable<IReadOnlyList<long>> struts)
    {
        long left = monitorBounds.Left;
        long top = monitorBounds.Top;
        long right = monitorBounds.Right;
        long bottom = monitorBounds.Bottom;

        foreach (var strut in struts)
        {
            if (strut.Count < 4)
            {
                continue;
            }

            // _NET_WM_STRUT has no edge ranges: the reservation spans the whole display edge.
            var hasRanges = strut.Count >= 12;

            if (strut[0] > 0 && RangesOverlap(
                    hasRanges ? strut[4] : displayBounds.Top,
                    hasRanges ? strut[5] : displayBounds.Bottom - 1,
                    monitorBounds.Top,
                    monitorBounds.Bottom - 1))
            {
                left = Math.Max(left, displayBounds.Left + strut[0]);
            }

            if (strut[1] > 0 && RangesOverlap(
                    hasRanges ? strut[6] : displayBounds.Top,
                    hasRanges ? strut[7] : displayBounds.Bottom - 1,
                    monitorBounds.Top,
                    monitorBounds.Bottom - 1))
            {
                right = Math.Min(right, displayBounds.Right - strut[1]);
            }

            if (strut[2] > 0 && RangesOverlap(
                    hasRanges ? strut[8] : displayBounds.Left,
                    hasRanges ? strut[9] : displayBounds.Right - 1,
                    monitorBounds.Left,
                    monitorBounds.Right - 1))
            {
                top = Math.Max(top, displayBounds.Top + strut[2]);
            }

            if (strut[3] > 0 && RangesOverlap(
                    hasRanges ? strut[10] : displayBounds.Left,
                    hasRanges ? strut[11] : displayBounds.Right - 1,
                    monitorBounds.Left,
                    monitorBounds.Right - 1))
            {
                bottom = Math.Min(bottom, displayBounds.Bottom - strut[3]);
            }
        }

        if (right - left < 1 || bottom - top < 1)
        {
            // The struts reserved the whole monitor (or are malformed); stay conservative.
            return monitorBounds;
        }

        return Rectangle.FromLTRB(checked((int)left), checked((int)top), checked((int)right), checked((int)bottom));
    }

    private static bool RangesOverlap(long firstStart, long firstEnd, long secondStart, long secondEnd)
    {
        return firstStart <= secondEnd && secondStart <= firstEnd;
    }
}
