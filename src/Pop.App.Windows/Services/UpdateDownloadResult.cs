namespace Pop.App.Windows.Services;

internal sealed record UpdateDownloadResult(UpdateDownloadOutcome Outcome, string Message, string? TargetVersion = null);
