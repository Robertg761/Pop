using Pop.Core.Models;

namespace Pop.Core.Services;

public sealed class WindowEligibilityEvaluator
{
    public WindowEligibilityResult Evaluate(WindowTraits traits)
    {
        if (!traits.IsCaptionHit)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.NotTitleBar);
        }

        if (!traits.IsVisible)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.NotVisible);
        }

        if (traits.IsCurrentProcessWindow)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.CurrentProcessWindow);
        }

        if (!traits.IsStandardTopLevelWindow)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.NotTopLevelDesktopWindow);
        }

        if (!traits.IsResizable)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.NotResizable);
        }

        if (traits.IsMinimized)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.Minimized);
        }

        if (traits.IsMaximized)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.Maximized);
        }

        if (traits.IsFullscreen)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.Fullscreen);
        }

        if (traits.IsCloaked)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.Cloaked);
        }

        if (traits.IsElevated)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.ElevatedProcess);
        }

        return WindowEligibilityResult.Supported();
    }
}
