using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;

namespace ChordDetection.Core.Music;

/// <summary>Transposition and capo arithmetic on pitch-class numbers.</summary>
public static class Transposer
{
    public const int MinTranspose = -6;
    public const int MaxTranspose = 6;
    public const int MinCapo = 0;
    public const int MaxCapo = 7;

    /// <summary>Shifts the root and the slash bass by <paramref name="semitones"/>; N.C. stays N.C.</summary>
    public static ChordResult Transpose(ChordResult chord, int semitones)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.IsNoChord || semitones % 12 == 0) return chord;
        return chord with
        {
            Root = PitchClasses.Of(chord.Root + semitones),
            Bass = chord.Bass is { } bass ? PitchClasses.Of(bass + semitones) : null,
        };
    }

    /// <summary>The shape to fret with a capo: sounding chord − capo (semitones).</summary>
    public static ChordResult Shape(ChordResult sounding, int capo) => Transpose(sounding, -capo);

    /// <summary>Detected chord → sounding chord after transposition → shape for the capo.</summary>
    public static ChordResult Shape(ChordResult detected, int transpose, int capo) =>
        Transpose(detected, transpose - capo);

    public static int ClampTranspose(int semitones) => Math.Clamp(semitones, MinTranspose, MaxTranspose);

    public static int ClampCapo(int capo) => Math.Clamp(capo, MinCapo, MaxCapo);
}
