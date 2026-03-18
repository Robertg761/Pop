namespace Pop.Core.Models;

public sealed record WindowTraits(
    bool IsCaptionHit,
    bool IsVisible,
    bool IsResizable,
    bool IsMinimized,
    bool IsMaximized,
    bool IsStandardTopLevelWindow,
    bool IsFullscreen,
    bool IsElevated,
    bool IsCloaked,
    bool IsCurrentProcessWindow);
