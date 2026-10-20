using ChordDetection.Core.Models;

namespace ChordDetection.Core.Notes;

/// <summary>
/// Stage 4: converts model probabilities into a list of notes.
/// Faithful port of basic_pitch.note_creation.output_to_notes_polyphonic (pitch bends are not needed and omitted).
/// </summary>
public sealed class NoteDecoder
{
    private const int Bins = BasicPitchConstants.NoteBins;
    private const int MaxFreqIndex = Bins - 1;

    private readonly NoteDecoderOptions _options;

    public NoteDecoder(NoteDecoderOptions? options = null)
    {
        _options = options ?? new NoteDecoderOptions();
        if (_options.OnsetThreshold <= 0 || _options.FrameThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Thresholds must be positive.");
    }

    /// <param name="firstFrame">
    /// Frame index of the first activation frame in the whole audio, for decoding only the tail of a stream;
    /// note times are then given in whole-audio time.
    /// </param>
    public IReadOnlyList<NoteEvent> Decode(NoteActivations activations, int firstFrame = 0)
    {
        ArgumentNullException.ThrowIfNull(activations);
        int n = activations.FrameCount;
        if (n == 0) return [];

        double frameThresh = _options.FrameThreshold;
        int minLen = _options.MinNoteLengthFrames;
        int tol = _options.EnergyTolerance;

        // float64 working copies, as numpy promotes them during processing.
        var frames = ToDouble(activations.Frames);
        var onsets = ToDouble(activations.Onsets);
        ConstrainFrequency(onsets, frames, n);

        if (_options.InferOnsets)
            onsets = InferOnsets(onsets, frames, n);

        var remaining = (double[])frames.Clone();
        var notes = new List<(int Start, int End, int Pitch, double Amp)>();

        // Onset peaks (scipy.signal.argrelmax along time; edges are never peaks),
        // visited backwards in time exactly like the reversed np.where result.
        for (int t = n - 2; t >= 1; t--)
        {
            for (int f = Bins - 1; f >= 0; f--)
            {
                double v = onsets[t * Bins + f];
                if (!(v > onsets[(t - 1) * Bins + f] && v > onsets[(t + 1) * Bins + f])) continue;
                if (v < _options.OnsetThreshold) continue;

                int start = t;
                if (start >= n - 1) continue;

                int i = start + 1;
                int k = 0;
                while (i < n - 1 && k < tol)
                {
                    if (remaining[i * Bins + f] < frameThresh) k++;
                    else k = 0;
                    i++;
                }
                i -= k;

                if (i - start <= minLen) continue;

                for (int x = start; x < i; x++)
                {
                    remaining[x * Bins + f] = 0;
                    if (f < MaxFreqIndex) remaining[x * Bins + f + 1] = 0;
                    if (f > 0) remaining[x * Bins + f - 1] = 0;
                }

                notes.Add((start, i, f + BasicPitchConstants.MidiOffset, Mean(frames, start, i, f)));
            }
        }

        if (_options.MelodiaTrick)
            ApplyMelodiaTrick(frames, remaining, n, notes, frameThresh, minLen, tol);

        return notes
            .Select(x => new NoteEvent(
                BasicPitchConstants.FrameToSeconds(firstFrame + x.Start),
                BasicPitchConstants.FrameToSeconds(firstFrame + x.End),
                x.Pitch,
                x.Amp))
            .ToList();
    }

    private static void ApplyMelodiaTrick(
        double[] frames, double[] remaining, int n,
        List<(int, int, int, double)> notes, double frameThresh, int minLen, int tol)
    {
        while (true)
        {
            // np.argmax: first occurrence of the maximum in row-major order.
            int argMax = 0;
            double max = remaining[0];
            for (int idx = 1; idx < remaining.Length; idx++)
            {
                if (remaining[idx] > max)
                {
                    max = remaining[idx];
                    argMax = idx;
                }
            }
            if (!(max > frameThresh)) break;

            int iMid = argMax / Bins;
            int f = argMax % Bins;
            remaining[argMax] = 0;

            // forward pass
            int i = iMid + 1;
            int k = 0;
            while (i < n - 1 && k < tol)
            {
                if (remaining[i * Bins + f] < frameThresh) k++;
                else k = 0;
                ClearWithNeighbours(remaining, i, f);
                i++;
            }
            int iEnd = i - 1 - k;

            // backward pass
            i = iMid - 1;
            k = 0;
            while (i > 0 && k < tol)
            {
                if (remaining[i * Bins + f] < frameThresh) k++;
                else k = 0;
                ClearWithNeighbours(remaining, i, f);
                i--;
            }
            int iStart = i + 1 + k;

            if (iEnd - iStart <= minLen) continue;

            notes.Add((iStart, iEnd, f + BasicPitchConstants.MidiOffset, Mean(frames, iStart, iEnd, f)));
        }
    }

    private static void ClearWithNeighbours(double[] remaining, int t, int f)
    {
        remaining[t * Bins + f] = 0;
        if (f < MaxFreqIndex) remaining[t * Bins + f + 1] = 0;
        if (f > 0) remaining[t * Bins + f - 1] = 0;
    }

    /// <summary>Port of get_infered_onsets (n_diff = 2).</summary>
    internal static double[] InferOnsets(double[] onsets, double[] frames, int n)
    {
        const int nDiff = 2;
        var frameDiff = new double[frames.Length];
        double maxDiff = 0;
        for (int t = 0; t < n; t++)
        {
            for (int f = 0; f < Bins; f++)
            {
                double d = double.MaxValue;
                if (t >= nDiff)
                {
                    for (int lag = 1; lag <= nDiff; lag++)
                    {
                        double prev = t - lag >= 0 ? frames[(t - lag) * Bins + f] : 0;
                        d = Math.Min(d, frames[t * Bins + f] - prev);
                    }
                    if (d < 0) d = 0;
                }
                else
                {
                    d = 0; // frame_diff[:n_diff, :] = 0
                }
                frameDiff[t * Bins + f] = d;
                if (d > maxDiff) maxDiff = d;
            }
        }

        double maxOnset = onsets.Length == 0 ? 0 : onsets.Max();
        var result = new double[onsets.Length];
        for (int idx = 0; idx < result.Length; idx++)
        {
            // Python divides by zero here (NaN) when the input is silent; we simply keep the onsets.
            double scaled = maxDiff > 0 ? maxOnset * frameDiff[idx] / maxDiff : 0;
            result[idx] = Math.Max(onsets[idx], scaled);
        }
        return result;
    }

    private void ConstrainFrequency(double[] onsets, double[] frames, int n)
    {
        int minIdx = 0;
        int maxIdx = Bins;
        if (_options.MinFrequencyHz is { } minHz) minIdx = HzToBin(minHz);
        if (_options.MaxFrequencyHz is { } maxHz) maxIdx = HzToBin(maxHz);
        if (minIdx <= 0 && maxIdx >= Bins) return;

        for (int t = 0; t < n; t++)
        {
            for (int f = 0; f < Bins; f++)
            {
                if (f < minIdx || f >= maxIdx)
                {
                    onsets[t * Bins + f] = 0;
                    frames[t * Bins + f] = 0;
                }
            }
        }
    }

    private static int HzToBin(double hz) =>
        (int)Math.Round(12 * Math.Log2(hz / 440.0) + 69 - BasicPitchConstants.MidiOffset, MidpointRounding.ToEven);

    private static double Mean(double[] frames, int start, int end, int f)
    {
        double sum = 0;
        for (int t = start; t < end; t++) sum += frames[t * Bins + f];
        return sum / (end - start);
    }

    private static double[] ToDouble(float[] src)
    {
        var dst = new double[src.Length];
        for (int i = 0; i < src.Length; i++) dst[i] = src[i];
        return dst;
    }
}
