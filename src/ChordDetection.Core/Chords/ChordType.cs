namespace ChordDetection.Core.Chords;

/// <summary>
/// One catalog entry, e.g. "m7" = {0, 3, 7, 10}.
/// </summary>
/// <param name="Symbol">Suffix written after the root ("" = major).</param>
/// <param name="Intervals">Semitones from the root. Must contain 0.</param>
/// <param name="Level">Display level the chord type belongs to (1 = basic, 2 = detailed).</param>
/// <param name="SimplifiesTo">Symbol shown when the display level is lower than <paramref name="Level"/>.</param>
/// <param name="OptionalFifth">
/// True when a missing perfect 5th is not penalised (7th chords, whose guitar voicings often leave it out).
/// Not for 6 / add9: C6 without G is C E A = Am/C.
/// </param>
public sealed record ChordType(
    string Symbol, IReadOnlyList<int> Intervals, int Level, string? SimplifiesTo = null, bool OptionalFifth = false)
{
    /// <summary>12-bit mask of the intervals (bit i = interval i).</summary>
    public int IntervalMask { get; } = Intervals.Aggregate(0, (mask, i) => mask | 1 << (((i % 12) + 12) % 12));

    public bool Contains(int interval) => (IntervalMask & 1 << (((interval % 12) + 12) % 12)) != 0;
}
