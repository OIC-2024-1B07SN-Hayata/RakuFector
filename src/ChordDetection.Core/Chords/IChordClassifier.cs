namespace ChordDetection.Core.Chords;

/// <summary>Stage 6: decides a chord name from the sounding pitch classes.</summary>
public interface IChordClassifier
{
    /// <summary>Always classifies at the most detailed level (design ③).</summary>
    ChordResult Classify(PitchClassProfile profile);
}
