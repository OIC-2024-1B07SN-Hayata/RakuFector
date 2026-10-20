using ChordDetection.Core.Models;
using ChordDetection.Core.Segmentation;

namespace ChordDetection.Core.Chords;

/// <summary>
/// Classifier input: strength of each of the 12 pitch classes (normalised so the strongest is 1)
/// and the pitch class of the bass note.
/// </summary>
public sealed class PitchClassProfile
{
    private readonly double[] _weights;

    public PitchClassProfile(IReadOnlyList<double> weights, int? bassPitchClass)
    {
        if (weights.Count != 12) throw new ArgumentException("12 weights are required.", nameof(weights));
        double max = weights.Max();
        _weights = weights.Select(w => max > 0 ? Math.Max(0, w) / max : 0).ToArray();
        BassPitchClass = bassPitchClass is { } b ? PitchClasses.Of(b) : null;
    }

    public int? BassPitchClass { get; }

    public double this[int pitchClass] => _weights[PitchClasses.Of(pitchClass)];

    public bool IsEmpty => _weights.All(w => w == 0);

    /// <summary>
    /// Builds a profile from a segment; note weights are overlap × amplitude.
    /// A pitch class takes the weight of its strongest note (not the sum over octaves), so that doubling the
    /// root an octave higher — as in a 3-string power chord — does not make the other chord tones look weak.
    /// </summary>
    /// <param name="harmonics">Weakens likely overtones first (see <see cref="HarmonicFilter"/>); null = no filtering.</param>
    public static PitchClassProfile FromSegment(NoteSegment segment, HarmonicFilter? harmonics = null)
    {
        var notes = harmonics is null ? segment.Notes : harmonics.Apply(segment.Notes);
        var w = new double[12];
        foreach (var n in notes)
        {
            int pc = PitchClasses.Of(n.Pitch);
            w[pc] = Math.Max(w[pc], n.Weight);
        }
        return new PitchClassProfile(w, segment.BassPitch);
    }

    /// <summary>
    /// Builds a profile from MIDI pitches with equal weight; the lowest pitch is the bass.
    /// Convenient for unit tests: FromPitches(40, 47, 55) = E minor.
    /// </summary>
    public static PitchClassProfile FromPitches(params int[] midiPitches)
    {
        var w = new double[12];
        foreach (var p in midiPitches) w[PitchClasses.Of(p)] = 1.0;
        return new PitchClassProfile(w, midiPitches.Length > 0 ? midiPitches.Min() : null);
    }

    public override string ToString() =>
        string.Join(" ", Enumerable.Range(0, 12).Where(i => _weights[i] > 0)
            .Select(i => $"{PitchClasses.Name(i)}:{_weights[i]:0.00}"));
}
