namespace LimboTranslate.Core;

public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = 0x20,
        ["enter"] = 0x0D,
        ["return"] = 0x0D,
        ["tab"] = 0x09,
        ["insert"] = 0x2D,
        ["delete"] = 0x2E,
        ["home"] = 0x24,
        ["end"] = 0x23,
        ["pageup"] = 0x21,
        ["pagedown"] = 0x22,
        ["up"] = 0x26,
        ["down"] = 0x28,
        ["left"] = 0x25,
        ["right"] = 0x27,
    };

    public static (uint Modifiers, uint VirtualKey) Parse(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            return (0, 0);

        string[] parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return (0, 0);

        uint modifiers = 0;
        uint virtualKey = 0;

        foreach (string part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    continue;
                case "shift":
                    modifiers |= ModShift;
                    continue;
                case "alt":
                    modifiers |= ModAlt;
                    continue;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    continue;
            }

            if (virtualKey != 0)
                return (0, 0);

            uint key = ParseKey(part);
            if (key == 0)
                return (0, 0);

            virtualKey = key;
        }

        if (virtualKey == 0)
            return (0, 0);

        return (modifiers, virtualKey);
    }

    public static bool IsDoubleCtrl(string? hotkey) =>
        string.Equals(hotkey?.Trim(), "DoubleCtrl", StringComparison.OrdinalIgnoreCase);

    private static uint ParseKey(string token)
    {
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                return c;
            return 0;
        }

        if (token.Length is 2 or 3 &&
            (token[0] == 'F' || token[0] == 'f') &&
            int.TryParse(token.AsSpan(1), out int number) &&
            number is >= 1 and <= 12)
        {
            return (uint)(0x70 + number - 1);
        }

        return NamedKeys.TryGetValue(token, out uint named) ? named : 0;
    }
}
