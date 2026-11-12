using ChordDetection.Core.Chords;

namespace ChordDetection.Core.Music;

/// <summary>Where the suggestions came from.</summary>
public enum CapoSuggestionSource
{
    /// <summary>No chords and no key yet: nothing to suggest.</summary>
    None,
    /// <summary>The representative chords of the estimated key.</summary>
    Key,
    /// <summary>Chords actually played (weighted by how often).</summary>
    History,
}

/// <summary>One capo position and the shapes to fret there.</summary>
/// <param name="Capo">Capo fret (0 = no capo).</param>
/// <param name="Score">Difficulty: lower is easier.</param>
/// <param name="Shapes">Shape names (always with #), in the order of the input chords.</param>
public sealed record CapoSuggestion(int Capo, double Score, IReadOnlyList<string> Shapes);

/// <summary>
/// Suggests capo positions: for every capo 0–7 the chords are turned into shapes and scored
/// (open chord 0, power chord 1, anything else such as barre chords 3, plus 0.3 per capo fret).
/// </summary>
public sealed class CapoAdvisor
{
    public const double OpenScore = 0;
    public const double PowerChordScore = 1;
    public const double OtherScore = 3;
    public const double PerCapoFret = 0.3;

    /// <summary>A key's representative chords are used until this many different chords have been played.</summary>
    public const int MinHistoryChords = 3;

    /// <summary>
    /// Shapes that can be played with open strings, written with # and the catalog symbols
    /// ("M7" = major 7th). Pass another set to the constructor to change it.
    /// </summary>
    public static IReadOnlySet<string> OpenShapes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // Major / minor
        "C", "D", "E", "G", "A", "Am", "Dm", "Em",
        // 7th
        "C7", "D7", "E7", "G7", "A7", "B7", "Am7", "Dm7", "Em7",
        "CM7", "DM7", "EM7", "FM7", "GM7", "AM7",
        // sus / add / 6
        "Dsus2", "Asus2", "Esus4", "Dsus4", "Asus4", "Cadd9", "A6", "D6", "E6",
    };

    private readonly IReadOnlySet<string> _openShapes;

    public CapoAdvisor(IReadOnlySet<string>? openShapes = null)
    {
        _openShapes = openShapes ?? OpenShapes;
    }

    /// <summary>Difficulty of fretting one shape (without the capo part).</summary>
    public double ShapeScore(ChordResult shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (shape.IsNoChord) return OpenScore;
        if (_openShapes.Contains(NoteSpeller.SharpChordName(shape with { Bass = null }))) return OpenScore;
        return shape.Type!.Symbol == "5" ? PowerChordScore : OtherScore;
    }

    /// <summary>
    /// Scores capo 0–7 for <paramref name="chords"/> (sounding chords and their weights) and returns the
    /// <paramref name="count"/> easiest; ties go to the lower capo. Empty when there are no chords.
    /// </summary>
    public IReadOnlyList<CapoSuggestion> Suggest(IEnumerable<(ChordResult Chord, int Weight)> chords, int count = 3)
    {
        ArgumentNullException.ThrowIfNull(chords);
        var input = chords.Where(c => !c.Chord.IsNoChord && c.Weight > 0).ToList();
        if (input.Count == 0) return [];

        return Enumerable.Range(Transposer.MinCapo, Transposer.MaxCapo - Transposer.MinCapo + 1)
            .Select(capo =>
            {
                var shapes = input.Select(c => (c.Weight, Shape: Transposer.Shape(c.Chord with { Bass = null }, capo))).ToList();
                double score = shapes.Sum(s => s.Weight * ShapeScore(s.Shape)) + capo * PerCapoFret;
                return new CapoSuggestion(capo, Math.Round(score, 6), [.. shapes.Select(s => NoteSpeller.SharpChordName(s.Shape))]);
            })
            .OrderBy(s => s.Score)
            .ThenBy(s => s.Capo)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Suggestions from the played chords once <see cref="MinHistoryChords"/> different chords have been
    /// played, otherwise from the key's representative chords. Everything is moved by
    /// <paramref name="transpose"/> first, so the suggestions fit the transposed (sounding) chords.
    /// </summary>
    public (CapoSuggestionSource Source, IReadOnlyList<CapoSuggestion> Suggestions) Suggest(
        ChordHistory history, MusicKey? key, int transpose = 0, int count = 3)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.DistinctCount >= MinHistoryChords)
        {
            var played = history.Entries.Select(e => (Transposer.Transpose(e.Chord, transpose), e.Count));
            return (CapoSuggestionSource.History, Suggest(played, count));
        }
        if (key is { } k)
        {
            var diatonic = RepresentativeChords(k.Transpose(transpose)).Select(c => (c, 1));
            return (CapoSuggestionSource.Key, Suggest(diatonic, count));
        }
        return (CapoSuggestionSource.None, []);
    }

    /// <summary>Major: I IIm IIIm IV V VIm. Minor: Im III IVm Vm VI VII.</summary>
    public static IReadOnlyList<ChordResult> RepresentativeChords(MusicKey key)
    {
        (int Offset, bool Minor)[] degrees = key.IsMinor
            ? [(0, true), (3, false), (5, true), (7, true), (8, false), (10, false)]
            : [(0, false), (2, true), (4, true), (5, false), (7, false), (9, true)];
        var catalog = ChordCatalog.Default;
        return [.. degrees.Select(d => new ChordResult(
            Models.PitchClasses.Of(key.Tonic + d.Offset), catalog[d.Minor ? "m" : ""], null, 0))];
    }
}
