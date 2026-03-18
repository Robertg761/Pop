namespace Pop.Core.Models;

public enum SnapRejectionReason
{
    None = 0,
    InsufficientSamples,
    InvalidSampleWindow,
    InsufficientVelocity,
    InsufficientHorizontalDominance
}
