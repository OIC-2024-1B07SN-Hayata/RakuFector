namespace ChordDetection.Core.Models;

/// <summary>
/// Frame-wise probabilities produced by the pitch model (stage 3 output, stage 4 input).
/// Both matrices are row-major: index = frame * <see cref="BasicPitchConstants.NoteBins"/> + bin.
/// </summary>
public sealed class NoteActivations
{
    public NoteActivations(float[] frames, float[] onsets)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(onsets);
        if (frames.Length != onsets.Length)
            throw new ArgumentException("frames and onsets must have the same length.");
        if (frames.Length % BasicPitchConstants.NoteBins != 0)
            throw new ArgumentException($"Length must be a multiple of {BasicPitchConstants.NoteBins}.");

        Frames = frames;
        Onsets = onsets;
    }

    /// <summary>"note" output: probability that a pitch is sounding.</summary>
    public float[] Frames { get; }

    /// <summary>"onset" output: probability that a pitch starts.</summary>
    public float[] Onsets { get; }

    public int FrameCount => Frames.Length / BasicPitchConstants.NoteBins;

    public static NoteActivations Empty { get; } = new([], []);
}
