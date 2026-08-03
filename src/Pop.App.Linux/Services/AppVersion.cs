namespace Pop.App.Linux.Services;

internal readonly struct AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private readonly int[] _components;
    private readonly string[] _prereleaseIdentifiers;

    private AppVersion(string value, int[] components, string[] prereleaseIdentifiers)
    {
        Value = value;
        _components = components;
        _prereleaseIdentifiers = prereleaseIdentifiers;
    }

    public string Value { get; }

    public static bool TryParse(string? value, out AppVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        var withoutPrefix = trimmed.StartsWith('v') || trimmed.StartsWith('V')
            ? trimmed[1..]
            : trimmed;
        var versionCore = withoutPrefix.Split('+', 2, StringSplitOptions.TrimEntries)[0];
        var releaseSplit = versionCore.Split('-', 2, StringSplitOptions.TrimEntries);
        var numericCore = releaseSplit[0];
        var prereleaseIdentifiers = releaseSplit.Length == 2 && releaseSplit[1].Length > 0
            ? releaseSplit[1].Split('.')
            : Array.Empty<string>();
        var parts = numericCore.Split('.', StringSplitOptions.None);

        if (parts.Length == 0)
        {
            return false;
        }

        var components = new int[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out var component) || component < 0)
            {
                return false;
            }

            components[index] = component;
        }

        version = new AppVersion(versionCore, components, prereleaseIdentifiers);
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        var componentCount = Math.Max(_components.Length, other._components.Length);
        for (var index = 0; index < componentCount; index++)
        {
            var left = index < _components.Length ? _components[index] : 0;
            var right = index < other._components.Length ? other._components[index] : 0;
            if (left != right)
            {
                return left.CompareTo(right);
            }
        }

        return ComparePrereleaseIdentifiers(_prereleaseIdentifiers, other._prereleaseIdentifiers);
    }

    private static int ComparePrereleaseIdentifiers(string[] left, string[] right)
    {
        // Semver: a pre-release version is lower than the same-numbered release.
        if (left.Length == 0 || right.Length == 0)
        {
            return right.Length.CompareTo(left.Length);
        }

        var identifierCount = Math.Min(left.Length, right.Length);
        for (var index = 0; index < identifierCount; index++)
        {
            var leftIsNumeric = long.TryParse(left[index], out var leftNumber);
            var rightIsNumeric = long.TryParse(right[index], out var rightNumber);

            var result = (leftIsNumeric, rightIsNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                (false, false) => string.CompareOrdinal(left[index], right[index])
            };

            if (result != 0)
            {
                return Math.Sign(result);
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    public bool Equals(AppVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        var componentCount = _components.Length;
        while (componentCount > 0 && _components[componentCount - 1] == 0)
        {
            componentCount--;
        }

        for (var index = 0; index < componentCount; index++)
        {
            hash.Add(_components[index]);
        }

        foreach (var identifier in _prereleaseIdentifiers)
        {
            hash.Add(identifier, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;
}
