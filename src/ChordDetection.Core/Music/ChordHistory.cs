using ChordDetection.Core.Chords;

namespace ChordDetection.Core.Music;

/// <summary>
/// Chords played recently, with how often each was played. A chord repeated back to back counts once
/// (C, C, G → C ×1, G ×1). The slash bass is ignored because it does not change the shape's difficulty.
/// </summary>
public sealed class ChordHistory
{
    private readonly Dictionary<(int Root, string Symbol), (ChordResult Chord, int Count)> _counts = [];
    private (int Root, string Symbol)? _last;

    /// <summary>Number of different chords recorded.</summary>
    public int DistinctCount => _counts.Count;

    /// <summary>Recorded chords (without bass) and how often each was played, in first-played order.</summary>
    public IReadOnlyList<(ChordResult Chord, int Count)> Entries => [.. _counts.Values];

    /// <summary>Records one chord; N.C. is skipped (it does not break a repeat either).</summary>
    public void Add(ChordResult chord)
    {
        ArgumentNullException.ThrowIfNull(chord);
        if (chord.IsNoChord) return;
        var id = (chord.Root, chord.Type!.Symbol);
        if (_last == id) return;
        _last = id;
        _counts[id] = _counts.TryGetValue(id, out var entry)
            ? (entry.Chord, entry.Count + 1)
            : (chord with { Bass = null }, 1);
    }

    public void AddRange(IEnumerable<ChordResult> chords)
    {
        ArgumentNullException.ThrowIfNull(chords);
        foreach (var chord in chords) Add(chord);
    }

    public void Reset()
    {
        _counts.Clear();
        _last = null;
    }
}
