using ChordDetection.Core.Segmentation;

namespace ChordDetection.Core.Chords;

public sealed record HarmonicFilterOptions
{
    /// <summary>
    /// Semitones above a lower note where its overtones fall and add a NEW pitch class:
    /// 28 = 5th harmonic (major 3rd), 34 = 7th (minor 7th), 38 = 9th (major 2nd), 40 = 10th (major 3rd).
    /// Octaves (12, 24, 36) and fifths (19, 31) are left alone: they repeat the root / fifth and are chord tones anyway.
    /// </summary>
    public IReadOnlyList<int> Intervals { get; init; } = [28, 34, 38, 40];

    /// <summary>A note counts as an overtone only when it is not stronger than the lower note × this (2 = up to twice as loud).</summary>
    public double MaxRelativeWeight { get; init; } = 2.0;

    /// <summary>Weight multiplier for notes that look like overtones (0 = remove, 1 = keep).</summary>
    public double Attenuation { get; init; } = 0.3;
}

/// <summary>
/// Weakens notes that are probably overtones of a lower note in the same segment.
/// Distortion makes overtones strong enough to be detected as notes; without this, an E5 power chord
/// gains G# (5th harmonic) and D (7th harmonic) and is classified as E7, or scores too low (N.C.).
/// </summary>
public sealed class HarmonicFilter
{
    private readonly HarmonicFilterOptions _options;

    public HarmonicFilter(HarmonicFilterOptions? options = null)
    {
        _options = options ?? new HarmonicFilterOptions();
    }

    /// <summary>Returns the notes with overtone weights reduced (pitches and order unchanged).</summary>
    public IReadOnlyList<SegmentNote> Apply(IReadOnlyList<SegmentNote> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var result = new List<SegmentNote>(notes.Count);
        foreach (var note in notes)
        {
            // Compare with the original weights so that attenuations do not cascade.
            bool overtone = notes.Any(lower =>
                _options.Intervals.Contains(note.Pitch - lower.Pitch) &&
                note.Weight <= lower.Weight * _options.MaxRelativeWeight);
            result.Add(overtone ? note with { Weight = note.Weight * _options.Attenuation } : note);
        }
        return result;
    }
}
