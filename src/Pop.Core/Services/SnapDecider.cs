using System.Drawing;
using Pop.Core.Interfaces;
using Pop.Core.Models;

namespace Pop.Core.Services;

public sealed class SnapDecider(Func<Point, MonitorInfo> monitorLookup) : ISnapDecider
{
    private const double CtrlCrossMonitorVelocityThresholdPxPerSec = 300d;
    private const double ProjectionWindowSeconds = 0.25d;
    private static readonly TimeSpan VelocityWindow = TimeSpan.FromMilliseconds(120);
    private readonly Func<Point, MonitorInfo> _monitorLookup = monitorLookup;

    public SnapDecision Decide(DragSession session, AppSettings settings)
    {
        if (!TryCalculateVelocity(session, out var metrics))
        {
            return metrics;
        }

        if (session.IsCtrlPressedAtRelease)
        {
            return DecideWithCtrl(session, settings, metrics);
        }

        return DecideWithoutCtrl(session, settings, metrics);
    }

    private static int FindFirstRelevantSampleIndex(IReadOnlyList<DragSample> samples, DateTimeOffset cutoff)
    {
        for (var index = samples.Count - 1; index >= 0; index--)
        {
            if (samples[index].Timestamp < cutoff)
            {
                return Math.Min(samples.Count - 1, index + 1);
            }
        }

        return 0;
    }

    private static bool TryCalculateVelocity(DragSession session, out SnapDecision metrics)
    {
        var fallbackPoint = session.ReleaseSample?.Position ?? session.Samples.LastOrDefault().Position;
        var releaseMonitor = session.CurrentMonitorInfo;

        if (session.Samples.Count < 2)
        {
            metrics = SnapDecision.None(SnapRejectionReason.InsufficientSamples, releaseMonitor, fallbackPoint);
            return false;
        }

        var lastSample = session.Samples[^1];
        var cutoff = lastSample.Timestamp - VelocityWindow;
        var firstIndex = FindFirstRelevantSampleIndex(session.Samples, cutoff);
        if (session.Samples.Count - firstIndex < 2)
        {
            firstIndex = Math.Max(0, session.Samples.Count - 4);
        }

        if (session.Samples.Count - firstIndex < 2)
        {
            metrics = SnapDecision.None(SnapRejectionReason.InsufficientSamples, releaseMonitor, lastSample.Position);
            return false;
        }

        var firstSample = session.Samples[firstIndex];
        var elapsedSeconds = (lastSample.Timestamp - firstSample.Timestamp).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            metrics = SnapDecision.None(SnapRejectionReason.InvalidSampleWindow, releaseMonitor, lastSample.Position);
            return false;
        }

        var horizontalVelocity = (lastSample.Position.X - firstSample.Position.X) / elapsedSeconds;
        var verticalVelocity = (lastSample.Position.Y - firstSample.Position.Y) / elapsedSeconds;
        var absHorizontal = Math.Abs(horizontalVelocity);
        var absVertical = Math.Abs(verticalVelocity);
        var dominanceRatio = absVertical < 1 ? absHorizontal : absHorizontal / absVertical;

        metrics = new SnapDecision(
            SnapTarget.None,
            releaseMonitor,
            lastSample.Position,
            horizontalVelocity,
            verticalVelocity,
            dominanceRatio,
            false,
            SnapRejectionReason.None);
        return true;
    }

    private SnapDecision DecideWithoutCtrl(DragSession session, AppSettings settings, SnapDecision metrics)
    {
        var absHorizontal = Math.Abs(metrics.HorizontalVelocityPxPerSec);
        if (absHorizontal < settings.ThrowVelocityThresholdPxPerSec)
        {
            return metrics with { RejectionReason = SnapRejectionReason.InsufficientVelocity };
        }

        if (metrics.HorizontalDominanceRatio < settings.HorizontalDominanceRatio)
        {
            return metrics with { RejectionReason = SnapRejectionReason.InsufficientHorizontalDominance };
        }

        var target = metrics.HorizontalVelocityPxPerSec < 0 ? SnapTarget.LeftHalf : SnapTarget.RightHalf;
        return metrics with
        {
            Target = target,
            TargetMonitorInfo = session.CurrentMonitorInfo,
            IsQualified = true,
            RejectionReason = SnapRejectionReason.None
        };
    }

    private SnapDecision DecideWithCtrl(DragSession session, AppSettings settings, SnapDecision metrics)
    {
        var dominantAxisVelocity = GetDominantAxisVelocity(metrics);
        if (dominantAxisVelocity < CtrlCrossMonitorVelocityThresholdPxPerSec)
        {
            return metrics with { RejectionReason = SnapRejectionReason.InsufficientVelocity };
        }

        if (GetDominantAxisDominanceRatio(metrics) < settings.HorizontalDominanceRatio)
        {
            return metrics with { RejectionReason = SnapRejectionReason.InsufficientHorizontalDominance };
        }

        var projectedLandingPoint = ProjectLandingPoint(session, metrics);
        var releaseMonitor = session.CurrentMonitorInfo;
        var targetMonitor = ResolveCtrlTargetMonitor(releaseMonitor, projectedLandingPoint, metrics);
        var target = targetMonitor == releaseMonitor || dominantAxisVelocity >= settings.ThrowVelocityThresholdPxPerSec
            ? DetermineTargetFromLandingX(projectedLandingPoint.X, targetMonitor)
            : DetermineTargetForSlowCrossMonitorThrow(releaseMonitor, targetMonitor, projectedLandingPoint);

        if (targetMonitor == releaseMonitor && dominantAxisVelocity < settings.ThrowVelocityThresholdPxPerSec)
        {
            return metrics with
            {
                TargetMonitorInfo = targetMonitor,
                ProjectedLandingPoint = projectedLandingPoint,
                RejectionReason = SnapRejectionReason.InsufficientVelocity
            };
        }

        return metrics with
        {
            Target = target,
            TargetMonitorInfo = targetMonitor,
            ProjectedLandingPoint = projectedLandingPoint,
            IsQualified = true,
            RejectionReason = SnapRejectionReason.None
        };
    }

    private MonitorInfo ResolveCtrlTargetMonitor(MonitorInfo releaseMonitor, Point projectedLandingPoint, SnapDecision metrics)
    {
        var targetMonitor = _monitorLookup(projectedLandingPoint);
        if (targetMonitor != MonitorInfo.Empty && targetMonitor != releaseMonitor)
        {
            return targetMonitor;
        }

        var probePoint = CreateDirectionalProbePoint(releaseMonitor, projectedLandingPoint, metrics);
        if (probePoint.HasValue)
        {
            targetMonitor = _monitorLookup(probePoint.Value);
            if (targetMonitor != MonitorInfo.Empty && targetMonitor != releaseMonitor)
            {
                return targetMonitor;
            }
        }

        return targetMonitor == MonitorInfo.Empty ? releaseMonitor : targetMonitor;
    }

    private static double GetDominantAxisVelocity(SnapDecision metrics)
    {
        return Math.Max(Math.Abs(metrics.HorizontalVelocityPxPerSec), Math.Abs(metrics.VerticalVelocityPxPerSec));
    }

    private static double GetDominantAxisDominanceRatio(SnapDecision metrics)
    {
        var absHorizontal = Math.Abs(metrics.HorizontalVelocityPxPerSec);
        var absVertical = Math.Abs(metrics.VerticalVelocityPxPerSec);
        var dominantAxis = Math.Max(absHorizontal, absVertical);
        var secondaryAxis = Math.Max(1d, Math.Min(absHorizontal, absVertical));

        return dominantAxis / secondaryAxis;
    }

    private static Point? CreateDirectionalProbePoint(MonitorInfo releaseMonitor, Point projectedLandingPoint, SnapDecision metrics)
    {
        var absHorizontal = Math.Abs(metrics.HorizontalVelocityPxPerSec);
        var absVertical = Math.Abs(metrics.VerticalVelocityPxPerSec);

        if (absVertical > absHorizontal)
        {
            var probeY = metrics.VerticalVelocityPxPerSec < 0
                ? releaseMonitor.Bounds.Top - 1
                : releaseMonitor.Bounds.Bottom + 1;

            return new Point(projectedLandingPoint.X, probeY);
        }

        if (absHorizontal > absVertical)
        {
            var probeX = metrics.HorizontalVelocityPxPerSec < 0
                ? releaseMonitor.Bounds.Left - 1
                : releaseMonitor.Bounds.Right + 1;

            return new Point(probeX, projectedLandingPoint.Y);
        }

        return null;
    }

    private static Point ProjectLandingPoint(DragSession session, SnapDecision metrics)
    {
        var currentBounds = session.CurrentBounds != Rectangle.Empty ? session.CurrentBounds : session.InitialBounds;
        var centerX = currentBounds.Left + (currentBounds.Width / 2d);
        var centerY = currentBounds.Top + (currentBounds.Height / 2d);

        return new Point(
            (int)Math.Round(centerX + (metrics.HorizontalVelocityPxPerSec * ProjectionWindowSeconds)),
            (int)Math.Round(centerY + (metrics.VerticalVelocityPxPerSec * ProjectionWindowSeconds)));
    }

    private static SnapTarget DetermineTargetForSlowCrossMonitorThrow(
        MonitorInfo releaseMonitor,
        MonitorInfo targetMonitor,
        Point projectedLandingPoint)
    {
        return TryDetermineClosestTargetToSource(releaseMonitor, targetMonitor, out var target)
            ? target
            : DetermineTargetFromLandingX(projectedLandingPoint.X, targetMonitor);
    }

    private static bool TryDetermineClosestTargetToSource(
        MonitorInfo releaseMonitor,
        MonitorInfo targetMonitor,
        out SnapTarget target)
    {
        target = SnapTarget.None;

        var horizontalOverlap = Math.Max(
            0,
            Math.Min(releaseMonitor.Bounds.Right, targetMonitor.Bounds.Right) -
            Math.Max(releaseMonitor.Bounds.Left, targetMonitor.Bounds.Left));
        var minimumWidth = Math.Min(releaseMonitor.Bounds.Width, targetMonitor.Bounds.Width);
        if (minimumWidth <= 0)
        {
            return false;
        }

        var overlapRatio = horizontalOverlap / (double)minimumWidth;
        if (overlapRatio >= 0.5d)
        {
            return false;
        }

        var releaseCenterX = releaseMonitor.Bounds.Left + (releaseMonitor.Bounds.Width / 2d);
        var targetCenterX = targetMonitor.Bounds.Left + (targetMonitor.Bounds.Width / 2d);
        if (Math.Abs(targetCenterX - releaseCenterX) < 1d)
        {
            return false;
        }

        target = targetCenterX > releaseCenterX ? SnapTarget.LeftHalf : SnapTarget.RightHalf;
        return true;
    }

    private static SnapTarget DetermineTargetFromLandingX(int landingX, MonitorInfo monitorInfo)
    {
        var midpoint = monitorInfo.WorkArea.Left + (monitorInfo.WorkArea.Width / 2d);
        return landingX < midpoint ? SnapTarget.LeftHalf : SnapTarget.RightHalf;
    }
}
