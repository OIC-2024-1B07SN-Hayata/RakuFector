using ChordDetection.Core;
using ChordDetection.Core.Models;
using static ChordDetection.Core.BasicPitchConstants;

namespace ChordDetection.Infrastructure.BasicPitch;

/// <summary>
/// Splits audio into 2-second model windows and stitches the outputs back together.
/// Port of inference.get_audio_input / window_audio_file / unwrap_output.
/// Kept separate from the ONNX session so it can be unit tested without the model.
/// </summary>
public static class BasicPitchWindowing
{
    /// <summary>
    /// Pads OverlapSamples/2 zeros at the start, then cuts windows of <see cref="WindowSamples"/>
    /// every <see cref="WindowHopSamples"/>. The last window is zero padded.
    /// </summary>
    public static IReadOnlyList<float[]> SplitIntoWindows(float[] audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        int pad = OverlapSamples / 2;
        int paddedLength = audio.Length + pad;

        var windows = new List<float[]>();
        for (int start = 0; start < paddedLength; start += WindowHopSamples)
        {
            var window = new float[WindowSamples];
            for (int i = 0; i < WindowSamples; i++)
            {
                int src = start + i - pad; // index into the unpadded audio
                if (src >= audio.Length) break;
                if (src >= 0) window[i] = audio[src];
            }
            windows.Add(window);
        }
        return windows;
    }

    /// <summary>
    /// Removes half of the overlapping frames on each side of every window, concatenates them
    /// and trims to the length of the original audio.
    /// </summary>
    /// <param name="windowOutputs">Per window: FramesPerWindow × bins, row-major.</param>
    /// <param name="bins">Bins per frame (88 for note/onset).</param>
    /// <param name="originalLength">Number of samples before padding.</param>
    public static float[] Unwrap(IReadOnlyList<float[]> windowOutputs, int bins, int originalLength)
    {
        const int nOlap = OverlapFrames / 2;
        const int keptPerWindow = FramesPerWindow - 2 * nOlap;

        int available = windowOutputs.Count * keptPerWindow;
        // Same expression as Python: int(audio_len / hop_size * (172 - 30))
        double expectedWindows = (double)originalLength / WindowHopSamples;
        int wanted = (int)(expectedWindows * (FramesPerWindow - OverlapFrames));
        int frames = Math.Min(available, wanted);

        var result = new float[frames * bins];
        int outFrame = 0;
        foreach (var w in windowOutputs)
        {
            if (w.Length != FramesPerWindow * bins)
                throw new ArgumentException($"Each window must have {FramesPerWindow * bins} values.");
            for (int f = nOlap; f < FramesPerWindow - nOlap && outFrame < frames; f++, outFrame++)
            {
                Array.Copy(w, f * bins, result, outFrame * bins, bins);
            }
        }
        return result;
    }

    public static NoteActivations Unwrap(
        IReadOnlyList<float[]> noteWindows, IReadOnlyList<float[]> onsetWindows, int originalLength) =>
        new(Unwrap(noteWindows, NoteBins, originalLength), Unwrap(onsetWindows, NoteBins, originalLength));
}
