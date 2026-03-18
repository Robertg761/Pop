namespace Pop.Platform.Abstractions.Shell;

// Future Windows tray and macOS menu-bar hosts can share this seam without leaking shell APIs into Pop.Core.
public interface IStatusItemHost : IDisposable
{
    void Show();
}
