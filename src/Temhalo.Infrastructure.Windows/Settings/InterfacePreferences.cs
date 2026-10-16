namespace Temhalo.Infrastructure.Windows.Settings;

/// <summary>Legacy overlay fields are ignored when reading older DPAPI documents.</summary>
public sealed record InterfacePreferences
{
    public int[] Presets { get; init; } = [0, 15, 30, 60];
    public void Validate()
    {
        if (Presets is null || Presets.Length is < 1 or > 8 || Presets[0] != 0 ||
            Presets.Any(value => value is < 0 or > 300) || Presets.Distinct().Count() != Presets.Length)
            throw new ArgumentException("Presets invalides.");
    }
}
