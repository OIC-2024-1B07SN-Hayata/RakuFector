namespace ChordDetection.Core.Chords;

/// <summary>A chord as it appears on screen (after simplification and merging).</summary>
public sealed record DisplayedChord(double StartSeconds, double EndSeconds, ChordResult Chord)
{
    public string Name => Chord.Name;
    public double Duration => EndSeconds - StartSeconds;
}

/// <summary>
/// Stage 7: converts detailed results to the selected display level.
/// Classification is always done at the detailed level; only this step changes when the user
/// switches the level, so no re-analysis is needed (design ③).
/// </summary>
public sealed class ChordSimplifier
{
    private readonly ChordCatalog _catalog;

    public ChordSimplifier(ChordCatalog? catalog = null)
    {
        _catalog = catalog ?? ChordCatalog.Default;
    }

    /// <param name="alsoShown">Symbols kept as they are even above <paramref name="level"/> (e.g. "m7", "sus4").</param>
    public ChordResult Simplify(ChordResult chord, DisplayLevel level, IReadOnlySet<string>? alsoShown = null)
    {
        if (chord.IsNoChord) return chord;

        var type = chord.Type!;
        // Follow the SimplifiesTo chain until the type fits the level.
        int guard = 0;
        while (type.Level > (int)level && alsoShown?.Contains(type.Symbol) != true)
        {
            if (type.SimplifiesTo is null || ++guard > 16) break;
            type = _catalog[type.SimplifiesTo];
        }

        // Slash bass is shown only at the detailed level.
        int? bass = level >= DisplayLevel.Detailed ? chord.Bass : null;
        return chord with { Type = type, Bass = bass };
    }

    /// <summary>
    /// Simplifies a timeline and merges neighbours that now have the same name
    /// (e.g. "C → Cmaj7" becomes one "C" at the basic level).
    /// </summary>
    public IReadOnlyList<DisplayedChord> SimplifyTimeline(
        IEnumerable<DetectedChord> chords, DisplayLevel level, double maxMergeGapSeconds = 0.5,
        IReadOnlySet<string>? alsoShown = null)
    {
        var result = new List<DisplayedChord>();
        foreach (var c in chords.OrderBy(c => c.StartSeconds))
        {
            var simplified = Simplify(c.Chord, level, alsoShown);
            if (result.Count > 0)
            {
                var last = result[^1];
                if (last.Name == simplified.Name && c.StartSeconds - last.EndSeconds <= maxMergeGapSeconds)
                {
                    result[^1] = last with { EndSeconds = Math.Max(last.EndSeconds, c.EndSeconds) };
                    continue;
                }
            }
            result.Add(new DisplayedChord(c.StartSeconds, c.EndSeconds, simplified));
        }
        return result;
    }
}
