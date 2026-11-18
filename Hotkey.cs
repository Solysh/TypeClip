namespace TypeClip;

/// <summary>A key plus an exact set of modifiers, parsed from text like "Ctrl+Shift+V".</summary>
sealed record Hotkey(bool Ctrl, bool Shift, bool Alt, bool Win, int Vk)
{
    public static readonly Hotkey Default = new(true, true, false, false, 'V');

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, shift = false, alt = false, win = false;
        int vk = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                case "win" or "windows": win = true; break;
                default:
                    if (vk != 0) return false; // two non-modifier keys
                    if (raw.Length == 1 && char.IsAsciiLetterOrDigit(raw[0])) vk = char.ToUpperInvariant(raw[0]);
                    else if (!char.IsDigit(raw[0]) && Enum.TryParse<Keys>(raw, true, out var k) && k != Keys.None) vk = (int)(k & Keys.KeyCode);
                    else return false;
                    break;
            }
        }

        if (vk == 0 || !(ctrl || shift || alt || win)) return false; // need a key and at least one modifier
        hotkey = new Hotkey(ctrl, shift, alt, win, vk);
        return true;
    }

    /// <summary>True when exactly this record's modifiers are currently held.</summary>
    public bool ModifiersMatch() =>
        Native.IsDown(0x11) == Ctrl &&
        Native.IsDown(0x10) == Shift &&
        Native.IsDown(0x12) == Alt &&
        (Native.IsDown(0x5B) || Native.IsDown(0x5C)) == Win;
}
