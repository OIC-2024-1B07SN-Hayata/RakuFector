using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;

namespace ChordDetection.Core.Music;

/// <summary>What a dot on the fretboard stands for. Later values win when a note is several of them.</summary>
public enum FretMarkKind
{
    ScaleNote,
    ScaleRoot,
    /// <summary>A note of the current chord that is not in the scale (key Am, chord E7: G#).</summary>
    ChordToneOutsideScale,
    ChordTone,
    ChordRoot,

    // Chord-form view (Fretboard.ChordFormMarks); not used by the scale view.
    /// <summary>The root, fretted in the chord form.</summary>
    FormRoot,
    /// <summary>Another chord tone, fretted in the chord form.</summary>
    FormTone,
    /// <summary>The root somewhere else on the neck.</summary>
    OtherRoot,
    /// <summary>Another chord tone somewhere else on the neck.</summary>
    OtherTone,
}

/// <summary>One dot. <paramref name="StringIndex"/> 0 is the 1st (highest) string.</summary>
public sealed record FretMark(int StringIndex, int Fret, int PitchClass, FretMarkKind Kind);

/// <summary>Where the notes of a scale (and of the current chord) are on a guitar neck.</summary>
public static class Fretboard
{
    /// <summary>Standard tuning, 1st string (E4) first, as MIDI numbers.</summary>
    public static IReadOnlyList<int> StandardTuning { get; } = [64, 59, 55, 50, 45, 40];

    public const int DefaultFretCount = 15;

    /// <summary>Pitch class at a string and fret.</summary>
    public static int PitchClassAt(int stringIndex, int fret, IReadOnlyList<int>? tuning = null) =>
        PitchClasses.Of((tuning ?? StandardTuning)[stringIndex] + fret);

    /// <summary>Pitch classes of a chord (root + intervals); empty for N.C. or null.</summary>
    public static IReadOnlySet<int> ChordTones(ChordResult? chord) =>
        chord is null || chord.IsNoChord
            ? new HashSet<int>()
            : chord.Type!.Intervals.Select(i => PitchClasses.Of(chord.Root + i)).ToHashSet();

    /// <summary>
    /// Dots for the frets from <paramref name="capo"/> (the capo acts as the nut) to <paramref name="fretCount"/>.
    /// Notes that are neither in the scale nor in the chord get no dot.
    /// </summary>
    public static IReadOnlyList<FretMark> Marks(
        IReadOnlySet<int> scale, int scaleRoot, ChordResult? chord, int capo = 0,
        int fretCount = DefaultFretCount, IReadOnlyList<int>? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(scale);
        tuning ??= StandardTuning;
        var chordTones = ChordTones(chord);
        int? chordRoot = chordTones.Count > 0 ? chord!.Root : null;
        int root = PitchClasses.Of(scaleRoot);

        var marks = new List<FretMark>();
        for (int s = 0; s < tuning.Count; s++)
        {
            for (int fret = Math.Max(0, capo); fret <= fretCount; fret++)
            {
                int pc = PitchClassAt(s, fret, tuning);
                FretMarkKind? kind =
                    pc == chordRoot ? FretMarkKind.ChordRoot
                    : chordTones.Contains(pc) ? (scale.Contains(pc) ? FretMarkKind.ChordTone : FretMarkKind.ChordToneOutsideScale)
                    : pc == root && scale.Contains(pc) ? FretMarkKind.ScaleRoot
                    : scale.Contains(pc) ? FretMarkKind.ScaleNote
                    : null;
                if (kind is { } k) marks.Add(new FretMark(s, fret, pc, k));
            }
        }
        return marks;
    }

    /// <summary>
    /// Dots for a chord form and the chord's notes elsewhere on the neck. The form's frets count from the capo
    /// (the capo acts as the nut), so <paramref name="sounding"/> is the chord that sounds (the form's shape + capo).
    /// </summary>
    public static IReadOnlyList<FretMark> ChordFormMarks(
        ChordForm form, ChordResult sounding, int capo = 0,
        int fretCount = DefaultFretCount, IReadOnlyList<int>? tuning = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(sounding);
        tuning ??= StandardTuning;
        var tones = ChordTones(sounding);
        if (tones.Count == 0) return [];
        int root = PitchClasses.Of(sounding.Root);
        capo = Math.Max(0, capo);

        var marks = new List<FretMark>();
        for (int s = 0; s < tuning.Count; s++)
        {
            int? formFret = form.FretOn(s) is { } f ? capo + f : null;
            for (int fret = capo; fret <= fretCount; fret++)
            {
                int pc = PitchClassAt(s, fret, tuning);
                bool inForm = fret == formFret;
                if (!inForm && !tones.Contains(pc)) continue;
                if (!inForm && formFret is null && fret == capo) continue; // a muted string shows ✕ there
                var kind = (inForm, pc == root) switch
                {
                    (true, true) => FretMarkKind.FormRoot,
                    (true, false) => FretMarkKind.FormTone,
                    (false, true) => FretMarkKind.OtherRoot,
                    _ => FretMarkKind.OtherTone,
                };
                marks.Add(new FretMark(s, fret, pc, kind));
            }
        }
        return marks;
    }
}
