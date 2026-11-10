using ChordDetection.Core.Models;

namespace ChordDetection.Core.Music;

/// <summary>A key: tonic pitch class (0 = C ... 11 = B) and major / minor.</summary>
public readonly record struct MusicKey(int Tonic, bool IsMinor)
{
    // Major keys written with flats: F, Bb, Eb, Ab, Db, Gb. Minor keys use the flats of their relative major.
    private static readonly int[] FlatMajorTonics = [5, 10, 3, 8, 1, 6];

    public int Tonic { get; init; } = PitchClasses.Of(Tonic);

    /// <summary>Tonic of the relative major (Am → C); the major key itself for a major key.</summary>
    public int RelativeMajorTonic => IsMinor ? PitchClasses.Of(Tonic + 3) : Tonic;

    /// <summary>True when notes of this key are written with b (F major, D minor …).</summary>
    public bool UsesFlats => FlatMajorTonics.Contains(RelativeMajorTonic);

    /// <summary>Relative key: Am ↔ C.</summary>
    public MusicKey Relative => IsMinor ? new(Tonic + 3, false) : new(Tonic + 9, true);

    public MusicKey Transpose(int semitones) => this with { Tonic = PitchClasses.Of(Tonic + semitones) };

    /// <summary>Key name such as "C" or "Dm", spelled for this key.</summary>
    public string Name => NoteSpeller.Name(Tonic, this) + (IsMinor ? "m" : "");

    /// <summary>Key name such as "D マイナー", spelled for this key.</summary>
    public string JapaneseName => $"{NoteSpeller.Name(Tonic, this)} {(IsMinor ? "マイナー" : "メジャー")}";

    public override string ToString() => Name;
}
