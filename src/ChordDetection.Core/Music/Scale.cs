using ChordDetection.Core.Models;

namespace ChordDetection.Core.Music;

/// <summary>Scales shown on the fretboard.</summary>
public enum ScaleType
{
    Major,
    NaturalMinor,
    MajorPentatonic,
    MinorPentatonic,
    Blues,
}

/// <summary>
/// Scales of a key. The root is chosen so that every scale stays inside the key:
/// major-type scales start on the (relative) major tonic, minor-type scales on the (relative) minor tonic
/// (key C with the minor pentatonic → A minor pentatonic).
/// </summary>
public static class Scale
{
    private static readonly string[] IntervalNames = ["R", "♭2", "2", "♭3", "3", "4", "♭5", "5", "♭6", "6", "♭7", "7"];

    /// <summary>Semitones above the scale root.</summary>
    public static IReadOnlyList<int> Intervals(ScaleType type) => type switch
    {
        ScaleType.Major => [0, 2, 4, 5, 7, 9, 11],
        ScaleType.NaturalMinor => [0, 2, 3, 5, 7, 8, 10],
        ScaleType.MajorPentatonic => [0, 2, 4, 7, 9],
        ScaleType.MinorPentatonic => [0, 3, 5, 7, 10],
        ScaleType.Blues => [0, 3, 5, 6, 7, 10],
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>True for scales that start on the minor tonic.</summary>
    public static bool IsMinorType(ScaleType type) =>
        type is ScaleType.NaturalMinor or ScaleType.MinorPentatonic or ScaleType.Blues;

    /// <summary>Root pitch class of <paramref name="type"/> in <paramref name="key"/>.</summary>
    public static int RootFor(MusicKey key, ScaleType type) =>
        IsMinorType(type)
            ? (key.IsMinor ? key.Tonic : key.Relative.Tonic)
            : key.RelativeMajorTonic;

    /// <summary>Pitch classes of the scale.</summary>
    public static IReadOnlySet<int> PitchClassesOf(MusicKey key, ScaleType type)
    {
        int root = RootFor(key, type);
        return Intervals(type).Select(i => PitchClasses.Of(root + i)).ToHashSet();
    }

    /// <summary>The scale picked when the key is decided: the key's own diatonic scale.</summary>
    public static ScaleType DefaultFor(MusicKey key) => key.IsMinor ? ScaleType.NaturalMinor : ScaleType.Major;

    public static string JapaneseName(ScaleType type) => type switch
    {
        ScaleType.Major => "メジャー・スケール",
        ScaleType.NaturalMinor => "ナチュラル・マイナー",
        ScaleType.MajorPentatonic => "メジャー・ペンタトニック",
        ScaleType.MinorPentatonic => "マイナー・ペンタトニック",
        ScaleType.Blues => "ブルース・スケール",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Interval label above a root, e.g. 3 → "♭3", 7 → "5".</summary>
    public static string IntervalName(int semitones) => IntervalNames[PitchClasses.Of(semitones)];
}
