namespace Temhalo.Infrastructure.Windows.Settings;

/// <summary>Optional UI extension of the v1 encrypted document; older settings remain readable.</summary>
public sealed record InterfacePreferences
{
    public int[] Presets { get; init; } = [0, 15, 30, 60];
    public int OverlayStyle { get; init; }
    public OverlayAppearance Signature { get; init; } = new();
    public OverlayAppearance Repere { get; init; } = new();
    public OverlayAppearance Fil { get; init; } = new();
    public OverlayAppearance Chrono { get; init; } = new();

    public OverlayAppearance Appearance(int style) => style switch
    { 0 => Signature, 1 => Repere, 2 => Fil, 3 => Chrono, _ => throw new ArgumentOutOfRangeException(nameof(style)) };
    public InterfacePreferences WithAppearance(int style, OverlayAppearance appearance) => style switch
    {
        0 => this with { Signature = appearance }, 1 => this with { Repere = appearance },
        2 => this with { Fil = appearance }, 3 => this with { Chrono = appearance },
        _ => throw new ArgumentOutOfRangeException(nameof(style))
    };
    public void Validate()
    {
        if (Presets is null || Presets.Length is < 1 or > 8 || Presets[0] != 0 ||
            Presets.Any(value => value is < 0 or > 300) || Presets.Distinct().Count() != Presets.Length ||
            OverlayStyle is < 0 or > 3 || Signature is null || Repere is null || Fil is null || Chrono is null)
            throw new ArgumentException("Presets ou composition d’overlay invalides.");
        foreach (var appearance in new[] { Signature, Repere, Fil, Chrono }) appearance.Validate();
    }
}

public sealed record OverlayAppearance
{
    public int Position { get; init; } = 1;
    public int ScalePercent { get; init; } = 100;
    public string Accent { get; init; } = "#7CE8CD";
    public string Label { get; init; } = "Différé";
    public bool VisibleWhenLive { get; init; } = true;
    public void Validate()
    {
        if (Position is < 0 or > 3 || ScalePercent is < 75 or > 150 || Label is null || Label.Length > 24 ||
            Label.Any(char.IsControl) || Accent is null || Accent.Length != 7 || Accent[0] != '#' ||
            !Accent.AsSpan(1).ToArray().All(Uri.IsHexDigit))
            throw new ArgumentException("L’overlay attend un libellé de 24 caractères maximum et une couleur #RRGGBB.");
    }
}
