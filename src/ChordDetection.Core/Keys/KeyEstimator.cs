using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;

namespace ChordDetection.Core.Keys;

/// <summary>Key of a whole analysis, decided by the final chord.</summary>
/// <param name="Tonic">Pitch class of the tonic (0 = C ... 11 = B).</param>
/// <param name="FinalChord">The chord the key was taken from.</param>
/// <param name="ModeFromProfile">
/// True when the final chord has no 3rd (power chord, sus) and major / minor was chosen from the whole performance.
/// </param>
public sealed record KeyResult(int Tonic, bool IsMinor, ChordResult FinalChord, bool ModeFromProfile)
{
    /// <summary>Key name such as "C" or "Am".</summary>
    public string Name => PitchClasses.Name(Tonic) + (IsMinor ? "m" : "");

    public string JapaneseName => $"{PitchClasses.Name(Tonic)} {(IsMinor ? "マイナー" : "メジャー")}";

    /// <summary>Relative key: Am ↔ C.</summary>
    public string RelativeName => IsMinor ? PitchClasses.Name(Tonic + 3) : PitchClasses.Name(Tonic + 9) + "m";

    public override string ToString() => Name;
}

/// <summary>
/// Decides the key from the last chord of the performance: its root is the tonic and its 3rd gives
/// major / minor. Only when the last chord has no 3rd (E5, Asus4 …) is major / minor taken from the
/// Krumhansl–Schmuckler profiles of the whole performance, with the tonic still fixed by the last chord.
/// </summary>
public sealed class KeyEstimator
{
    // Krumhansl & Kessler (1982) probe-tone ratings, index 0 = tonic.
    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    /// <summary>Final chords shorter than this are taken as noise or a passing chord and skipped.</summary>
    public const double MinFinalChordSeconds = 0.3;

    /// <summary>Longest time one note counts for in the profile, so that a ringing open string does not dominate.</summary>
    public const double MaxNoteSeconds = 2.0;

    /// <summary>Returns null when no chord was found.</summary>
    public KeyResult? Estimate(ChordAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        var chords = analysis.Chords.Where(c => !c.Chord.IsNoChord).ToList();
        var final = chords.LastOrDefault(c => c.EndSeconds - c.StartSeconds >= MinFinalChordSeconds) ?? chords.LastOrDefault();
        if (final is null) return null;

        var chord = final.Chord;
        var type = chord.Type!;
        if (type.Contains(3) != type.Contains(4))
            return new KeyResult(chord.Root, IsMinor: type.Contains(3), chord, ModeFromProfile: false);

        var histogram = analysis.Notes.Count > 0 ? FromNotes(analysis.Notes) : FromChords(chords);
        bool minor = Correlate(histogram, MinorProfile, chord.Root) > Correlate(histogram, MajorProfile, chord.Root);
        return new KeyResult(chord.Root, minor, chord, ModeFromProfile: true);
    }

    /// <summary>Duration (capped at <see cref="MaxNoteSeconds"/>) × amplitude per pitch class.</summary>
    public static double[] FromNotes(IEnumerable<NoteEvent> notes)
    {
        var h = new double[12];
        foreach (var n in notes) h[n.PitchClass] += Math.Min(n.Duration, MaxNoteSeconds) * n.Amplitude;
        return h;
    }

    /// <summary>Chord tones weighted by chord length (the chroma method returns no notes).</summary>
    public static double[] FromChords(IEnumerable<DetectedChord> chords)
    {
        var h = new double[12];
        foreach (var c in chords.Where(c => !c.Chord.IsNoChord))
        {
            double length = c.EndSeconds - c.StartSeconds;
            foreach (int interval in c.Chord.Type!.Intervals)
                h[PitchClasses.Of(c.Chord.Root + interval)] += length;
        }
        return h;
    }

    private static double Correlate(IReadOnlyList<double> histogram, double[] profile, int tonic)
    {
        double meanH = histogram.Average(), meanP = profile.Average();
        double num = 0, varH = 0, varP = 0;
        for (int i = 0; i < 12; i++)
        {
            double x = histogram[(i + tonic) % 12] - meanH, y = profile[i] - meanP;
            num += x * y;
            varH += x * x;
            varP += y * y;
        }
        return varH > 0 ? num / Math.Sqrt(varH * varP) : 0;
    }
}
