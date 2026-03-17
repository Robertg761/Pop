using System.Reflection;

namespace Pop.App;

internal static class AppReleaseMetadata
{
    private static readonly Assembly Assembly = typeof(AppReleaseMetadata).Assembly;

    public static string ProductName =>
        Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "Pop";

    public static string CurrentVersion =>
        NormalizeVersion(
            Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetName().Version?.ToString())
        ?? "0.0.0";

    public static string RepositoryUrl => GetMetadata("PopRepositoryUrl") ?? "https://github.com/Robertg761/Pop";

    public static string VelopackPackId => GetMetadata("PopVelopackPackId") ?? "Robertg761.Pop";

    public static string Authors => GetMetadata("PopAuthors") ?? "Robertg761";

    public static string PublishRuntimeIdentifier => GetMetadata("PopPublishRuntimeIdentifier") ?? "win-x64";

    public static string VelopackVersion => GetMetadata("PopVelopackVersion") ?? "0.0.1298";

    private static string? GetMetadata(string key) =>
        Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;

    internal static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        return version.Split('+', 2, StringSplitOptions.TrimEntries)[0];
    }
}
