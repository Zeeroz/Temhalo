namespace Temhalo.Infrastructure.Windows.Hotkeys;

public enum HotkeyAction { Protection, Zero, Increase, Decrease }

public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    public static HotkeyGesture Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64) throw Invalid();
        string[] parts = value.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            uint modifier = parts[i].ToUpperInvariant() switch
            { "ALT" => 1, "CTRL" or "CONTROL" => 2, "SHIFT" or "MAJ" => 4, "WIN" => 8, _ => 0 };
            if (modifier == 0 || (modifiers & modifier) != 0) throw Invalid();
            modifiers |= modifier;
        }
        if (modifiers == 0) throw new ArgumentException("Ajoutez au moins Ctrl, Alt, Maj ou Win au raccourci.");
        string key = parts[^1].ToUpperInvariant();
        uint vk = key.Length == 1 && key[0] is >= 'A' and <= 'Z' or >= '0' and <= '9' ? key[0] :
            key.StartsWith('F') && int.TryParse(key.AsSpan(1), out int function) && function is >= 1 and <= 11 ? (uint)(111 + function) :
            key switch { "UP" => 0x26, "DOWN" => 0x28, "LEFT" => 0x25, "RIGHT" => 0x27,
                "HOME" => 0x24, "END" => 0x23, "PAGEUP" => 0x21, "PAGEDOWN" => 0x22, "INSERT" => 0x2D, _ => 0 };
        if (vk == 0) throw Invalid();
        return new(modifiers, vk);
    }
    private static ArgumentException Invalid() => new("Raccourci attendu : Ctrl+Alt+F6, Maj+F8 ou Ctrl+Alt+P. Touches : A–Z, 0–9, F1–F11, flèches, Home, End, PageUp, PageDown, Insert. F12 est réservé à Windows.");
}
