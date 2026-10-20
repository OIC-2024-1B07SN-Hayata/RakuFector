using ChordDetection.Core.Models;

namespace ChordDetection.Core.Chords;

/// <summary>Parses chord names such as "C#m7/E" (used for reference labels in accuracy measurement).</summary>
public sealed class ChordNameParser
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["maj"] = "", ["M"] = "", ["min"] = "m", ["-"] = "m",
        ["maj7"] = "M7", ["Maj7"] = "M7", ["min7"] = "m7", ["-7"] = "m7",
        ["dom7"] = "7", ["+"] = "aug", ["°"] = "dim", ["sus"] = "sus4",
        ["m7♭5"] = "m7b5", ["m7-5"] = "m7b5", ["ø"] = "m7b5", ["ø7"] = "m7b5", ["add2"] = "add9",
    };

    private readonly ChordCatalog _catalog;

    public ChordNameParser(ChordCatalog? catalog = null)
    {
        _catalog = catalog ?? ChordCatalog.Default;
    }

    public bool TryParse(string text, out ChordResult chord)
    {
        chord = ChordResult.NoChord;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text is "N.C." or "N" or "NC") return true;

        string main = text;
        int? bass = null;
        int slash = text.IndexOf('/');
        if (slash >= 0)
        {
            main = text[..slash];
            if (!PitchClasses.TryParse(text[(slash + 1)..], out int b)) return false;
            bass = b;
        }

        if (main.Length == 0) return false;
        int rootLength = main.Length > 1 && (main[1] == '#' || main[1] == 'b') ? 2 : 1;
        if (!PitchClasses.TryParse(main[..rootLength], out int root)) return false;

        string symbol = main[rootLength..];
        if (Aliases.TryGetValue(symbol, out var alias)) symbol = alias;
        if (!_catalog.TryGet(symbol, out var type)) return false;

        chord = new ChordResult(root, type, bass ?? root, 0);
        return true;
    }
}
