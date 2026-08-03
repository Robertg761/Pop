namespace Pop.App.Linux.Services;

internal readonly struct AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    private readonly int[] _components;

    private AppVersion(string value, int[] components)
    {
        Value = value;
        _components = components;
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
        var numericCore = versionCore.Split('-', 2, StringSplitOptions.TrimEntries)[0];
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

        version = new AppVersion(numericCore, components);
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

        return 0;
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

        return hash.ToHashCode();
    }

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;
}
