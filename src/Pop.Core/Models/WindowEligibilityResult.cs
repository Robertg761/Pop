namespace Pop.Core.Models;

public sealed record WindowEligibilityResult(bool IsSupported, WindowEligibilityReason Reason, string? Detail = null)
{
    public static WindowEligibilityResult Supported(string? detail = null) => new(true, WindowEligibilityReason.Supported, detail);

    public static WindowEligibilityResult Unsupported(WindowEligibilityReason reason, string? detail = null) => new(false, reason, detail);
}
