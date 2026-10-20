namespace ChordDetection.Core.Notes;

/// <summary>
/// Parameters of <see cref="NoteDecoder"/>.
/// Defaults are identical to basic_pitch.inference.predict() except the two thresholds,
/// which are raised to keep noise from becoming notes (official: onset 0.5, frame 0.3),
/// and the upper frequency limit, which removes high overtones (official: none).
/// </summary>
public sealed record NoteDecoderOptions
{
    /// <summary>Minimum onset activation to start a note (official default 0.5).</summary>
    public double OnsetThreshold { get; init; } = 0.6;

    /// <summary>Minimum frame activation for a note to stay "on" (official default 0.3).</summary>
    public double FrameThreshold { get; init; } = 0.4;

    /// <summary>Minimum note length in milliseconds (official default 127.70).</summary>
    public double MinNoteLengthMs { get; init; } = 127.70;

    /// <summary>Add onsets inferred from sudden increases of frame activation.</summary>
    public bool InferOnsets { get; init; } = true;

    /// <summary>Recover notes without a detected onset ("melodia trick").</summary>
    public bool MelodiaTrick { get; init; } = true;

    /// <summary>Frames below threshold tolerated before a note is closed.</summary>
    public int EnergyTolerance { get; init; } = 11;

    /// <summary>
    /// Lowest frequency kept (Hz). null = no limit (official default).
    /// 78 removes distortion difference tones below E2 (E1 etc.) that would become the bass; drop D needs 70, 7-string 58.
    /// </summary>
    public double? MinFrequencyHz { get; init; }

    /// <summary>
    /// Highest frequency kept (Hz). null = no limit (official default).
    /// 1000 Hz removes most overtones while keeping the notes of chords in the lower positions
    /// (bins from round(midi(1000 Hz)) = B5 upwards are dropped, as in the official code, so A#5 932 Hz is the top note).
    /// </summary>
    public double? MaxFrequencyHz { get; init; } = 1000;

    /// <summary>
    /// For tone-shaped (distorted / compressed) guitar. Distortion lowers Basic Pitch's probabilities for the played
    /// notes to about 0.2–0.4 (measured), so they fall below the default 0.4 and chords become N.C.
    /// Frame 0.2 recovered every chord of a driven test song while clean input stayed correct; onset 0.5
    /// (official) worked better than 0.4 or 0.3. Use together with <see cref="Segmentation.NoteSegmenterOptions.ToneShaped"/>.
    /// </summary>
    public static NoteDecoderOptions ToneShaped { get; } = new() { OnsetThreshold = 0.5, FrameThreshold = 0.2 };

    /// <summary>Minimum note length converted to model frames (np.round = banker's rounding).</summary>
    public int MinNoteLengthFrames =>
        (int)Math.Round(MinNoteLengthMs / 1000.0 * ((double)BasicPitchConstants.SampleRate / BasicPitchConstants.FftHop),
            MidpointRounding.ToEven);
}
