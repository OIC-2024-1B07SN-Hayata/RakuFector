using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;

namespace ChordDetection.Core.Music;

/// <summary>Which note is Ⅰ in a minor key.</summary>
public enum MinorDegreeBase
{
    /// <summary>The tonic of the relative major (key Am: C = Ⅰ, Am = Ⅵm), as in most J-POP explanations.</summary>
    RelativeMajor,

    /// <summary>The minor tonic itself (key Am: Am = Ⅰm, C = ♭Ⅲ).</summary>
    MinorTonic,
}

/// <summary>
/// Chord names as degrees of a key: Ⅰ, Ⅱm7, Ⅴ7, ♭Ⅶ, Ⅰ/Ⅲ … The chord type is written as in the chord name
/// (m, 7, M7, m7b5 …). Transposing chord and key together keeps the degree.
/// </summary>
public static class DegreeNamer
{
    // Semitones above Ⅰ. Notes outside the major scale use ♭, except the tritone, which is written ♯Ⅳ.
    private static readonly string[] Degrees = ["Ⅰ", "♭Ⅱ", "Ⅱ", "♭Ⅲ", "Ⅲ", "Ⅳ", "♯Ⅳ", "Ⅴ", "♭Ⅵ", "Ⅵ", "♭Ⅶ", "Ⅶ"];

    /// <summary>Pitch class that is Ⅰ in <paramref name="key"/>.</summary>
    public static int TonicOf(MusicKey key, MinorDegreeBase minorBase = MinorDegreeBase.RelativeMajor) =>
        key.IsMinor && minorBase == MinorDegreeBase.MinorTonic ? key.Tonic : key.RelativeMajorTonic;

    /// <summary>Degree of one note, e.g. 10 → "♭Ⅶ" in C.</summary>
    public static string Name(int pitchClass, MusicKey key, MinorDegreeBase minorBase = MinorDegreeBase.RelativeMajor) =>
        Degrees[PitchClasses.Of(pitchClass - TonicOf(key, minorBase))];

    /// <summary>Chord as a degree, e.g. G7 → "Ⅴ7", C/E → "Ⅰ/Ⅲ" in C; "N.C." stays as it is.</summary>
    public static string Degree(ChordResult chord, MusicKey key, MinorDegreeBase minorBase = MinorDegreeBase.RelativeMajor)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.IsNoChord) return ChordResult.NoChordName;
        var name = Name(chord.Root, key, minorBase) + chord.Type!.Symbol;
        return chord.IsInversion ? $"{name}/{Name(chord.Bass!.Value, key, minorBase)}" : name;
    }
}
