namespace ChordDetection.Core.Chords;

public sealed record ChordScoringOptions
{
    /// <summary>Normalised weight from which a pitch class counts as "present".</summary>
    public double PresenceThreshold { get; init; } = 0.15;

    /// <summary>Points per chord tone, multiplied by its weight.</summary>
    public double MatchWeight { get; init; } = 1.0;

    /// <summary>Penalty for each chord tone that is not present.</summary>
    public double MissingPenalty { get; init; } = 1.0;

    /// <summary>Penalty per non-chord tone, multiplied by its weight.</summary>
    public double ExtraPenalty { get; init; } = 1.0;

    /// <summary>Bonus when the bass note is the root.</summary>
    public double BassRootBonus { get; init; } = 0.5;

    /// <summary>
    /// Results scoring below this become N.C. A full triad scores 3 (3.5 with the root in the bass),
    /// a chord missing one tone about 1.5, so noise and incomplete chords are rejected.
    /// A 7th chord without the 5th is accepted through <see cref="OptionalFifthInSeventhChords"/>.
    /// </summary>
    public double MinScore { get; init; } = 2.0;

    /// <summary>
    /// True: a missing perfect 5th is not penalised in chord types marked <see cref="ChordType.OptionalFifth"/> (7, M7, m7). Guitar voicings often leave it out
    /// (open C7 x32310 = C E Bb C E); with the penalty they scored about 1.3–1.8 on real recordings and became N.C.
    /// </summary>
    public bool OptionalFifthInSeventhChords { get; init; } = true;

    /// <summary>Fewer present pitch classes than this → N.C. (a single note is not a chord).</summary>
    public int MinPitchClasses { get; init; } = 2;
}

/// <summary>
/// Stage 6: scores every (root × chord type) pair against the dictionary and returns the best one.
/// score = Σ match(weight) − missing tones − Σ extra(weight) + bass bonus.
/// </summary>
public sealed class RuleBasedChordClassifier : IChordClassifier
{
    private readonly ChordCatalog _catalog;
    private readonly ChordScoringOptions _options;

    public RuleBasedChordClassifier(ChordCatalog? catalog = null, ChordScoringOptions? options = null)
    {
        _catalog = catalog ?? ChordCatalog.Default;
        _options = options ?? new ChordScoringOptions();
    }

    public ChordResult Classify(PitchClassProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        int present = Enumerable.Range(0, 12).Count(pc => profile[pc] >= _options.PresenceThreshold);
        if (present < _options.MinPitchClasses) return ChordResult.NoChord;

        ChordResult? best = null;
        for (int root = 0; root < 12; root++)
        {
            foreach (var type in _catalog.Types)
            {
                double score = Score(profile, root, type);
                var candidate = new ChordResult(root, type, profile.BassPitchClass, score);
                if (best is null || IsBetter(candidate, best)) best = candidate;
            }
        }

        return best is null || best.Score < _options.MinScore ? ChordResult.NoChord : best;
    }

    /// <summary>Score of one candidate. Public so that the UI or tests can show the reasoning.</summary>
    public double Score(PitchClassProfile profile, int root, ChordType type)
    {
        double score = 0;
        for (int pc = 0; pc < 12; pc++)
        {
            double w = profile[pc];
            int interval = ((pc - root) % 12 + 12) % 12;
            if (type.Contains(interval))
            {
                score += _options.MatchWeight * w;
                if (w < _options.PresenceThreshold && !IsOptionalFifth(type, interval)) score -= _options.MissingPenalty;
            }
            else
            {
                score -= _options.ExtraPenalty * w;
            }
        }

        if (profile.BassPitchClass == root) score += _options.BassRootBonus;
        return score;
    }

    private bool IsOptionalFifth(ChordType type, int interval) =>
        _options.OptionalFifthInSeventhChords && interval == 7 && type.OptionalFifth;

    /// <summary>Higher score wins; ties prefer bass = root, then simpler chords.</summary>
    private static bool IsBetter(ChordResult a, ChordResult b)
    {
        const double eps = 1e-9;
        if (a.Score > b.Score + eps) return true;
        if (a.Score < b.Score - eps) return false;

        bool aBass = a.Bass == a.Root, bBass = b.Bass == b.Root;
        if (aBass != bBass) return aBass;
        if (a.Type!.Level != b.Type!.Level) return a.Type.Level < b.Type.Level;
        return a.Type.Intervals.Count < b.Type.Intervals.Count;
    }
}
