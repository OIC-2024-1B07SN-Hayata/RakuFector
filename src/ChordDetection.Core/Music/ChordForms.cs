using System.Globalization;
using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;

namespace ChordDetection.Core.Music;

/// <summary>
/// One way to fret a chord. <paramref name="Frets"/> is in TAB order, 6th string first (as in "x02230");
/// null = muted, 0 = open string.
/// </summary>
public sealed record ChordForm(IReadOnlyList<int?> Frets)
{
    /// <summary>"x02230"; with a fret of 10 or more the frets are separated ("x-12-14-14-13-x").</summary>
    public string Text
    {
        get
        {
            var parts = Frets.Select(f => f is { } v ? v.ToString(CultureInfo.InvariantCulture) : "x").ToList();
            return parts.Any(p => p.Length > 1) ? string.Join("-", parts) : string.Concat(parts);
        }
    }

    /// <summary>Fret on a string; <paramref name="stringIndex"/> 0 is the 1st (highest) string, as in <see cref="Fretboard"/>.</summary>
    public int? FretOn(int stringIndex) => Frets[Frets.Count - 1 - stringIndex];

    /// <summary>Lowest fretted (not open) fret; 0 when only open strings are played.</summary>
    public int MinFret => Frets.Where(f => f > 0).Select(f => f!.Value).DefaultIfEmpty(0).Min();

    /// <summary>Highest fret played (0 for open strings only).</summary>
    public int MaxFret => Frets.Where(f => f is not null).Select(f => f!.Value).DefaultIfEmpty(0).Max();

    /// <summary>Lowest fret played, counting open strings as 0. Forms are ordered by it.</summary>
    public int Position => Frets.Where(f => f is not null).Select(f => f!.Value).DefaultIfEmpty(0).Min();

    /// <summary>Parses "x02230" (one character per string) or "x-12-14-14-13-x".</summary>
    public static ChordForm Parse(string text)
    {
        var parts = text.Contains('-') ? text.Split('-') : text.Select(c => c.ToString()).ToArray();
        if (parts.Length != 6) throw new FormatException($"'{text}' is not a 6-string form.");
        return new ChordForm([.. parts.Select(p => p is "x" or "X" ? (int?)null : int.Parse(p, CultureInfo.InvariantCulture))]);
    }

    public override string ToString() => Text;
}

/// <summary>
/// Chord forms for standard tuning: the usual open chords, then movable forms with the root on the
/// 6th string (E form), the 5th string (A form) and the 4th string (D form), moved to the root.
/// </summary>
public static class ChordForms
{
    /// <summary>Open chords, keyed by root pitch class and catalog symbol (the same set as <see cref="CapoAdvisor.OpenShapes"/>).</summary>
    private static readonly Dictionary<(int Root, string Symbol), string> OpenForms = new()
    {
        [(0, "")] = "x32010", [(2, "")] = "xx0232", [(4, "")] = "022100", [(7, "")] = "320003", [(9, "")] = "x02220",
        [(9, "m")] = "x02210", [(2, "m")] = "xx0231", [(4, "m")] = "022000",
        [(0, "7")] = "x32310", [(2, "7")] = "xx0212", [(4, "7")] = "020100", [(7, "7")] = "320001",
        [(9, "7")] = "x02020", [(11, "7")] = "x21202",
        [(9, "m7")] = "x02010", [(2, "m7")] = "xx0211", [(4, "m7")] = "020000",
        [(0, "M7")] = "x32000", [(2, "M7")] = "xx0222", [(4, "M7")] = "021100", [(5, "M7")] = "xx3210",
        [(7, "M7")] = "320002", [(9, "M7")] = "x02120",
        [(2, "sus2")] = "xx0230", [(9, "sus2")] = "x02200",
        [(4, "sus4")] = "022200", [(2, "sus4")] = "xx0233", [(9, "sus4")] = "x02230",
        [(0, "add9")] = "x32030",
        [(9, "6")] = "x02222", [(2, "6")] = "xx0202", [(4, "6")] = "022120",
    };

    /// <summary>String (6th = 0 … 1st = 5 in TAB order) that holds the root of each movable form.</summary>
    private enum FormBase { E, A, D }

    /// <summary>Movable forms relative to the root fret, in TAB order (6th string first).</summary>
    private static readonly Dictionary<string, (FormBase Base, string Frets)[]> Movable = new(StringComparer.Ordinal)
    {
        [""] = [(FormBase.E, "022100"), (FormBase.A, "x02220"), (FormBase.D, "xx0232")],
        ["m"] = [(FormBase.E, "022000"), (FormBase.A, "x02210"), (FormBase.D, "xx0231")],
        ["5"] = [(FormBase.E, "022xxx"), (FormBase.A, "x022xx"), (FormBase.D, "xx023x")],
        ["7"] = [(FormBase.E, "020100"), (FormBase.A, "x02020"), (FormBase.D, "xx0212")],
        ["M7"] = [(FormBase.E, "0x110x"), (FormBase.A, "x02120"), (FormBase.D, "xx0222")],
        ["m7"] = [(FormBase.E, "020000"), (FormBase.A, "x02010"), (FormBase.D, "xx0211")],
        ["sus2"] = [(FormBase.A, "x02200"), (FormBase.D, "xx0230")],
        ["sus4"] = [(FormBase.E, "022200"), (FormBase.A, "x02230"), (FormBase.D, "xx0233")],
        ["dim"] = [(FormBase.E, "0120xx"), (FormBase.A, "x0121x"), (FormBase.D, "xx0131")],
        ["aug"] = [(FormBase.E, "032110"), (FormBase.A, "x03221"), (FormBase.D, "xx0332")],
        ["m7b5"] = [(FormBase.E, "01203x"), (FormBase.A, "x0101x"), (FormBase.D, "xx0111")],
        ["6"] = [(FormBase.E, "022120"), (FormBase.A, "x02222"), (FormBase.D, "xx0202")],
        ["add9"] = [(FormBase.E, "022102")],
    };

    /// <summary>
    /// Forms for <paramref name="chord"/> (its slash bass is ignored), easiest first: the open chord, then the movable
    /// forms from the lowest position up. Empty for N.C. or a chord type without forms.
    /// </summary>
    public static IReadOnlyList<ChordForm> For(ChordResult chord, int maxFret = Fretboard.DefaultFretCount)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.IsNoChord) return [];
        int root = PitchClasses.Of(chord.Root);
        string symbol = chord.Type!.Symbol;

        var forms = new List<ChordForm>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(ChordForm form)
        {
            if (form.MaxFret <= maxFret && seen.Add(form.Text)) forms.Add(form);
        }

        if (OpenForms.TryGetValue((root, symbol), out var open)) Add(ChordForm.Parse(open));

        if (Movable.TryGetValue(symbol, out var templates))
        {
            var moved = new List<ChordForm>();
            foreach (var (formBase, frets) in templates)
            {
                var template = ChordForm.Parse(frets);
                int openPitch = Fretboard.StandardTuning[Fretboard.StandardTuning.Count - 1 - (int)formBase];
                for (int offset = PitchClasses.Of(root - openPitch); offset <= maxFret; offset += 12)
                    moved.Add(new ChordForm([.. template.Frets.Select(f => f + offset)]));
            }
            foreach (var form in moved.OrderBy(f => f.Position)) Add(form); // stable: E, A, D form on a tie
        }
        return forms;
    }
}
