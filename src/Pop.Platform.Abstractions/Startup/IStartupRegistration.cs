namespace Pop.Platform.Abstractions.Startup;

public interface IStartupRegistration
{
    /// <summary>
    /// Applies the launch-at-startup preference to the OS. Returns <c>true</c> when the
    /// registration was written or removed, <c>false</c> when it could not be applied (e.g. the
    /// registry/plist write failed) so the caller can keep the persisted flag in sync with reality.
    /// </summary>
    bool TrySetLaunchAtStartup(bool enabled);

    /// <summary>
    /// Reads whether launch-at-startup is currently registered with the OS, or <c>null</c> when
    /// the state cannot be determined.
    /// </summary>
    bool? IsLaunchAtStartupEnabled();
}
