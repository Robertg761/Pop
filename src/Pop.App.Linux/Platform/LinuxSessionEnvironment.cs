namespace Pop.App.Linux.Platform;

internal static class LinuxSessionEnvironment
{
    /// <summary>
    /// True when this is a Wayland session. On Wayland the X11 path only ever sees XWayland
    /// windows, so snapping native windows would silently no-op; callers must not fall back to
    /// X11 silently. Setting POP_FORCE_X11=1 opts back into the XWayland-only path deliberately.
    /// </summary>
    public static bool IsWaylandSession()
    {
        var forceX11 = Environment.GetEnvironmentVariable("POP_FORCE_X11");
        if (string.Equals(forceX11, "1", StringComparison.Ordinal) ||
            string.Equals(forceX11, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        return string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) ||
               !string.IsNullOrWhiteSpace(waylandDisplay);
    }
}
