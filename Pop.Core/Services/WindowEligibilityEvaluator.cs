using Pop.Core.Models;

namespace Pop.Core.Services;

public sealed class WindowEligibilityEvaluator
{
    public WindowEligibilityResult Evaluate(WindowTraits traits)
    {
        if (!traits.IsCaptionHit)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.NotTitleBar, "Pointer was not over a title bar.");
        }

        if (!traits.IsVisible)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.NotVisible, "Window is not visible.");
        }

        if (traits.IsCurrentProcessWindow)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.CurrentProcessWindow, "Pop ignores its own windows.");
        }

        if (!traits.IsStandardTopLevelWindow)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.NotTopLevelDesktopWindow, "Window is not a standard top-level desktop window.");
        }

        if (!traits.IsResizable)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.NotResizable, "Window does not expose a resizable frame.");
        }

        if (traits.IsMinimized)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.Minimized, "Minimized windows cannot be thrown into tiles.");
        }

        if (traits.IsMaximized)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.Maximized, "Maximized windows are ignored.");
        }

        if (traits.IsFullscreen)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.Fullscreen, "Fullscreen windows are ignored.");
        }

        if (traits.IsCloaked)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.Cloaked, "Cloaked windows are ignored.");
        }

        if (traits.IsElevated)
        {
            return WindowEligibilityResult.Unsupported(WindowEligibilityReason.ElevatedProcess, "Elevated windows are not managed in v1.");
        }

        return WindowEligibilityResult.Supported("Window is eligible for Pop momentum snapping.");
    }
}
