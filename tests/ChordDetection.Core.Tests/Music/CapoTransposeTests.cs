using ChordDetection.Core.Chords;
using ChordDetection.Core.Music;

namespace ChordDetection.Core.Tests.Music;

public class CapoTransposeTests
{
    private static readonly ChordNameParser Parser = new();
    private static readonly MusicKey CMajor = new(0, false);
    private static readonly MusicKey FMajor = new(5, false);
    private static readonly MusicKey DMinor = new(2, true);

    /// <summary>Builds a chord from a name such as "F#m" or "C/E".</summary>
    private static ChordResult Chord(string name)
    {
        Assert.True(Parser.TryParse(name, out var chord), name);
        return chord;
    }

    // ---------- transposition ----------

    [Theory]
    [InlineData("C", 2, "D")]
    [InlineData("C", -1, "B")]
    [InlineData("C/E", 2, "D/F#")]
    public void Transpose_MovesRootAndBass(string chord, int semitones, string expected) =>
        Assert.Equal(expected, NoteSpeller.ChordName(Transposer.Transpose(Chord(chord), semitones)));

    [Fact]
    public void Transpose_NoChordStaysNoChord() =>
        Assert.True(Transposer.Transpose(ChordResult.NoChord, 3).IsNoChord);

    // ---------- spelling ----------

    [Fact]
    public void Spelling_FMajorUsesFlats()
    {
        Assert.Equal("Bb", NoteSpeller.ChordName(Chord("A#"), FMajor));
        Assert.Equal("Bb", NoteSpeller.Name(10, FMajor));
    }

    [Fact]
    public void Spelling_DMinorUsesFlats() =>
        Assert.Equal("Bb", NoteSpeller.ChordName(Chord("A#"), DMinor));

    [Fact]
    public void Spelling_UnknownKeyUsesSharps() =>
        Assert.Equal("A#", NoteSpeller.ChordName(Chord("Bb"), key: null));

    [Fact]
    public void Spelling_KeyNameFollowsKey()
    {
        Assert.Equal("Bb", new MusicKey(10, false).Name);
        Assert.Equal("D マイナー", DMinor.JapaneseName);
        Assert.Equal("F", DMinor.Relative.Name);
    }

    // ---------- capo ----------

    [Fact]
    public void Capo_ShapeIsSoundingMinusCapo() =>
        Assert.Equal("G", NoteSpeller.SharpChordName(Transposer.Shape(Chord("A"), capo: 2)));

    [Fact]
    public void Capo_ShapeAlwaysUsesSharps() =>
        Assert.Equal("A#", NoteSpeller.SharpChordName(Transposer.Shape(Chord("C"), capo: 2)));

    [Fact]
    public void TransposeAndCapo_Together()
    {
        // A transposed +2 sounds B; with capo 2 it is fretted as an A shape.
        var detected = Chord("A");
        Assert.Equal("B", NoteSpeller.ChordName(Transposer.Transpose(detected, 2)));
        Assert.Equal("A", NoteSpeller.SharpChordName(Transposer.Shape(detected, transpose: 2, capo: 2)));
        // C transposed −1 sounds B; with capo 4 it is fretted as G.
        Assert.Equal("G", NoteSpeller.SharpChordName(Transposer.Shape(Chord("C"), transpose: -1, capo: 4)));
    }

    // ---------- capo suggestions ----------

    [Fact]
    public void Suggest_ADEFsharpm_BestIsCapo2()
    {
        var chords = new[] { "A", "D", "E", "F#m" }.Select(n => (Chord(n), 1));
        var best = new CapoAdvisor().Suggest(chords)[0];
        Assert.Equal(2, best.Capo);
        Assert.Equal(["G", "C", "D", "Em"], best.Shapes);
    }

    [Fact]
    public void Suggest_ReturnsTopThreeEasiestFirst()
    {
        var chords = new[] { "A", "D", "E", "F#m" }.Select(n => (Chord(n), 1));
        var suggestions = new CapoAdvisor().Suggest(chords);
        Assert.Equal(3, suggestions.Count);
        Assert.True(suggestions[0].Score <= suggestions[1].Score && suggestions[1].Score <= suggestions[2].Score);
    }

    [Fact]
    public void Suggest_CMajorKey_BestIsNoCapo()
    {
        var (source, suggestions) = new CapoAdvisor().Suggest(new ChordHistory(), CMajor);
        Assert.Equal(CapoSuggestionSource.Key, source);
        Assert.Equal(0, suggestions[0].Capo);
    }

    [Fact]
    public void Suggest_ScoresFollowTheRules()
    {
        var advisor = new CapoAdvisor();
        Assert.Equal(CapoAdvisor.OpenScore, advisor.ShapeScore(Chord("G")));
        Assert.Equal(CapoAdvisor.PowerChordScore, advisor.ShapeScore(Chord("F5")));
        Assert.Equal(CapoAdvisor.OtherScore, advisor.ShapeScore(Chord("F")));
        // E5 at capo 1 = F5 shape: 1 + 0.3.
        var only = advisor.Suggest([(Chord("F#5"), 1)], count: 8).Single(s => s.Capo == 1);
        Assert.Equal(1.3, only.Score, 6);
    }

    [Fact]
    public void Suggest_OpenShapesCanBeReplaced()
    {
        var advisor = new CapoAdvisor(new HashSet<string> { "F" });
        Assert.Equal(CapoAdvisor.OpenScore, advisor.ShapeScore(Chord("F")));
        Assert.Equal(CapoAdvisor.OtherScore, advisor.ShapeScore(Chord("G")));
    }

    [Fact]
    public void Suggest_NothingKnown_IsEmpty()
    {
        var (source, suggestions) = new CapoAdvisor().Suggest(new ChordHistory(), key: null);
        Assert.Equal(CapoSuggestionSource.None, source);
        Assert.Empty(suggestions);
    }

    [Fact]
    public void Suggest_UsesKeyUntilThreeDifferentChords_ThenHistory()
    {
        var advisor = new CapoAdvisor();
        var history = new ChordHistory();
        history.AddRange([Chord("A"), Chord("D"), Chord("A"), Chord("D")]);
        Assert.Equal(2, history.DistinctCount);
        Assert.Equal(CapoSuggestionSource.Key, advisor.Suggest(history, CMajor).Source);

        history.AddRange([Chord("E"), Chord("F#m")]);
        var (source, suggestions) = advisor.Suggest(history, CMajor);
        Assert.Equal(CapoSuggestionSource.History, source);
        Assert.Equal(2, suggestions[0].Capo);
    }

    [Fact]
    public void Suggest_ResetHistory_BackToKey()
    {
        var advisor = new CapoAdvisor();
        var history = new ChordHistory();
        history.AddRange([Chord("A"), Chord("D"), Chord("E")]);
        Assert.Equal(CapoSuggestionSource.History, advisor.Suggest(history, CMajor).Source);

        history.Reset();
        Assert.Equal(0, history.DistinctCount);
        Assert.Equal(CapoSuggestionSource.Key, advisor.Suggest(history, CMajor).Source);
    }

    [Fact]
    public void Suggest_FollowsTransposition()
    {
        // C major transposed +2 = D major (D Em F#m G A Bm): F#m and Bm are barre chords, so a capo helps.
        var (_, suggestions) = new CapoAdvisor().Suggest(new ChordHistory(), CMajor, transpose: 2);
        Assert.NotEqual(0, suggestions[0].Capo);
        // Capo 2 on D major gives the C major shapes.
        Assert.Contains(suggestions, s => s.Capo == 2 && s.Shapes.SequenceEqual(["C", "Dm", "Em", "F", "G", "Am"]));
    }

    // ---------- history ----------

    [Fact]
    public void History_RepeatedChordCountsOnce()
    {
        var history = new ChordHistory();
        history.AddRange([Chord("C"), Chord("C"), ChordResult.NoChord, Chord("C"), Chord("G"), Chord("C")]);
        var counts = history.Entries.ToDictionary(e => NoteSpeller.ChordName(e.Chord), e => e.Count);
        Assert.Equal(2, counts["C"]);
        Assert.Equal(1, counts["G"]);
    }

    [Fact]
    public void History_WeightsByCount()
    {
        // G F C F → G ×1, F ×2, C ×1. Without a capo only F is a barre chord, counted twice: 2 × 3 = 6.
        var history = new ChordHistory();
        history.AddRange([Chord("G"), Chord("F"), Chord("C"), Chord("F")]);
        var (source, _) = new CapoAdvisor().Suggest(history, key: null);
        Assert.Equal(CapoSuggestionSource.History, source);
        var noCapo = new CapoAdvisor().Suggest(history.Entries.Select(e => (e.Chord, e.Count)), count: 8).Single(s => s.Capo == 0);
        Assert.Equal(2 * CapoAdvisor.OtherScore, noCapo.Score, 6);
    }
}
