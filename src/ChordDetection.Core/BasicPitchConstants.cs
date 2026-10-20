namespace ChordDetection.Core;

/// <summary>
/// Constants of the Basic Pitch (ICASSP 2022) model.
/// Values mirror basic_pitch/constants.py and basic_pitch/inference.py of the official implementation.
/// </summary>
public static class BasicPitchConstants
{
    /// <summary>Sample rate the model expects (Hz).</summary>
    public const int SampleRate = 22050;

    /// <summary>Hop between two model frames, in samples.</summary>
    public const int FftHop = 256;

    /// <summary>Model frames per second (22050 // 256 = 86, integer division as in Python).</summary>
    public const int FramesPerSecond = SampleRate / FftHop;

    /// <summary>Input length of one inference window, in samples (2 s minus one hop).</summary>
    public const int WindowSamples = SampleRate * 2 - FftHop; // 43844

    /// <summary>Output frames per inference window.</summary>
    public const int FramesPerWindow = FramesPerSecond * 2; // 172

    /// <summary>Overlapping frames between consecutive windows.</summary>
    public const int OverlapFrames = 30;

    /// <summary>Overlap between consecutive windows, in samples.</summary>
    public const int OverlapSamples = OverlapFrames * FftHop; // 7680

    /// <summary>Hop between consecutive windows, in samples.</summary>
    public const int WindowHopSamples = WindowSamples - OverlapSamples; // 36164

    /// <summary>Number of pitch bins in the note/onset outputs (piano range A0..C8).</summary>
    public const int NoteBins = 88;

    /// <summary>MIDI number of pitch bin 0 (A0).</summary>
    public const int MidiOffset = 21;

    private const double MagicAlignmentOffset = 0.0018;

    /// <summary>
    /// Converts a model frame index to seconds.
    /// Port of note_creation.model_frames_to_time (includes the per-window alignment correction).
    /// </summary>
    public static double FrameToSeconds(int frame)
    {
        double original = frame * (double)FftHop / SampleRate;
        int windowNumber = frame / FramesPerWindow;
        double windowOffset = (double)FftHop / SampleRate
                              * (FramesPerWindow - (double)WindowSamples / FftHop)
                              + MagicAlignmentOffset;
        return original - windowOffset * windowNumber;
    }
}
