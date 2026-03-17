namespace Pop.Core.Models;

public sealed record WindowEligibilityResult(bool IsSupported, WindowEligibilityReason Reason)
{
    public static WindowEligibilityResult Supported() => new(true, WindowEligibilityReason.Supported);

    public static WindowEligibilityResult Unsupported(WindowEligibilityReason reason) => new(false, reason);
}
