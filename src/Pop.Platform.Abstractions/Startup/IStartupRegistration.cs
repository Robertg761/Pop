namespace Pop.Platform.Abstractions.Startup;

public interface IStartupRegistration
{
    void SetLaunchAtStartup(bool enabled);
}
