using ChordDetection.Core.Chords;

namespace ChordDetection.Core.Music;

/// <summary>Chords of one bar for the chord sheet.</summary>
/// <param name="Index">Zero-based bar index (bar number − 1).</param>
/// <param name="Chords">One chord for the whole bar, or two chords for the first and second half; empty for N.C.</param>
public sealed record BarChords(int Index, double StartSeconds, double EndSeconds, IReadOnlyList<ChordResult> Chords)
{
    public int Number => Index + 1;
    public bool IsEmpty => Chords.Count == 0;
}

/// <summary>
/// Turns the time-based chord timeline into one entry per bar, so it reads like a chord sheet
/// (| C | Am | F G | C |).
/// <list type="bullet">
/// <item>The chord sounding longest in a bar is taken for it.</item>
/// <item>When a second chord sounds for at least <see cref="SplitShare"/> of the bar, the bar is split in two halves,
/// in the order the two chords are played.</item>
/// <item>A bar where chords sound for less than <see cref="MinCoverage"/> of it is N.C. (empty).</item>
/// </list>
/// </summary>
public sealed class BarChordQuantizer
{
    /// <summary>Share of the bar a second chord needs to split the bar into halves.</summary>
    public const double SplitShare = 0.35;

    /// <summary>Share of the bar that must be covered by chords; otherwise the bar is N.C.</summary>
    public const double MinCoverage = 0.25;

    /// <param name="chords">Displayed chords (N.C. entries are ignored).</param>
    /// <param name="durationSeconds">Length of the audio; bars after it are not produced.</param>
    /// <param name="trimEmptyEdges">True to drop empty bars before the first and after the last chord
    /// (bar numbers are kept, so bar 1 stays the first beat of the metronome).</param>
    public IReadOnlyList<BarChords> Quantize(IReadOnlyList<DisplayedChord> chords, BarGrid grid, double durationSeconds,
        bool trimEmptyEdges = true)
    {
        ArgumentNullException.ThrowIfNull(chords);
        ArgumentNullException.ThrowIfNull(grid);

        var sounding = chords.Where(c => !c.Chord.IsNoChord && c.EndSeconds > c.StartSeconds).ToList();
        int count = grid.BarCount(durationSeconds);
        var bars = new List<BarChords>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            double start = grid.BarStart(i), end = start + grid.BarSeconds;
            bars.Add(new BarChords(i, start, end, ChordsInBar(sounding, start, end)));
        }

        if (!trimEmptyEdges) return bars;
        int first = bars.FindIndex(b => !b.IsEmpty);
        if (first < 0) return [];
        int last = bars.FindLastIndex(b => !b.IsEmpty);
        return bars.GetRange(first, last - first + 1);
    }

    private static IReadOnlyList<ChordResult> ChordsInBar(List<DisplayedChord> chords, double start, double end)
    {
        double length = end - start;
        // Per chord name: time sounding in the bar and the time-weighted centre (to know which comes first).
        var shares = new Dictionary<string, (ChordResult Chord, double Seconds, double WeightedCentre)>(StringComparer.Ordinal);
        double covered = 0;
        foreach (var c in chords)
        {
            double s = Math.Max(start, c.StartSeconds), e = Math.Min(end, c.EndSeconds);
            if (e <= s) continue;
            double seconds = e - s;
            covered += seconds;
            string key = c.Chord.Name;
            var (chord, total, centre) = shares.TryGetValue(key, out var v) ? v : (c.Chord, 0, 0);
            shares[key] = (chord, total + seconds, centre + seconds * (s + e) / 2);
        }
        if (covered < length * MinCoverage) return [];

        var ranked = shares.Values.OrderByDescending(v => v.Seconds).ToList();
        var top = ranked[0];
        if (ranked.Count < 2 || ranked[1].Seconds < length * SplitShare) return [top.Chord];

        var second = ranked[1];
        return top.WeightedCentre / top.Seconds <= second.WeightedCentre / second.Seconds
            ? [top.Chord, second.Chord]
            : [second.Chord, top.Chord];
    }
}
