using System.Reflection;

namespace Pop.App.Linux;

internal static class AppReleaseMetadata
{
    private static readonly Assembly Assembly = typeof(AppReleaseMetadata).Assembly;

    public static string CurrentVersion =>
        NormalizeVersion(
            Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetName().Version?.ToString())
        ?? "0.0.0";

    public static string RepositoryUrl => GetMetadata("PopRepositoryUrl") ?? "https://github.com/Robertg761/Pop";

    internal static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return version.Split('+', 2, StringSplitOptions.TrimEntries)[0];
    }

    private static string? GetMetadata(string key) =>
        Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;
}
