using System.Text.RegularExpressions;

namespace Temhalo.Infrastructure.Windows.Deployment;

/// <summary>The stable/preview version format shared by the application, tags and NSIS.</summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, int? Preview) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = null!;
        if (value is null || value.Length > 60) return false;
        var match = Regex.Match(value, @"\Av?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-preview\.(0|[1-9]\d*))?\z", RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var parts = new int[4];
        for (int i = 1; i <= 4; i++)
            if (match.Groups[i].Success && (!int.TryParse(match.Groups[i].Value, out parts[i - 1]) || parts[i - 1] > 65535)) return false;
        // 65535 is reserved for stable releases by Build-Installer.ps1.
        if (match.Groups[4].Success && parts[3] == 65535) return false;
        version = new(parts[0], parts[1], parts[2], match.Groups[4].Success ? parts[3] : null);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        int comparison = Major.CompareTo(other.Major);
        if (comparison == 0) comparison = Minor.CompareTo(other.Minor);
        if (comparison == 0) comparison = Patch.CompareTo(other.Patch);
        return comparison != 0 ? comparison : (Preview ?? 65535).CompareTo(other.Preview ?? 65535);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Preview is { } number ? $"-preview.{number}" : "");
}
