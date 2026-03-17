namespace Pop.App.Services;

internal sealed record UpdateDownloadResult(UpdateDownloadOutcome Outcome, string Message, string? TargetVersion = null);
