using ChordDetection.Core;

namespace ChordDetection.Infrastructure.Audio;

/// <summary>
/// Stage 2: converts any input to what Basic Pitch expects: mono, 22,050 Hz.
/// The official implementation uses librosa.load(sr=22050, mono=True); this is a
/// dependency-free equivalent (channel average + Kaiser-windowed sinc resampling).
/// A noise gate then silences the parts where the guitar is not played.
/// Works on a whole file (<see cref="Process"/>) or piece by piece (<see cref="CreateStream"/>) with identical output.
/// </summary>
public sealed class AudioPreprocessor
{
    public int TargetSampleRate { get; } = BasicPitchConstants.SampleRate;

    /// <summary>
    /// Blocks whose RMS level is below this (dBFS) are silenced, so that hum and hiss while the
    /// guitar is not played do not become notes. null disables the gate.
    /// </summary>
    public double? NoiseGateThresholdDb { get; init; } = -50;

    /// <summary>How long the gate stays open after the level falls (keeps the decay of a chord).</summary>
    public double NoiseGateHoldSeconds { get; init; } = 0.2;

    public float[] Process(AudioData audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var stream = CreateStream(audio.SampleRate, audio.Channels);
        return [.. stream.Process(audio.Samples), .. stream.Complete()];
    }

    /// <summary>Preprocesses audio that arrives piece by piece (all pieces share the rate and channel count).</summary>
    public PreprocessorStream CreateStream(int sampleRate, int channels) =>
        new(sampleRate, channels, TargetSampleRate,
            NoiseGateThresholdDb is { } db ? new NoiseGate(TargetSampleRate, db, NoiseGateHoldSeconds) : null);

    /// <summary>Preprocesses a sequence of pieces, yielding mono 22,050 Hz samples as soon as they are final.</summary>
    public IEnumerable<float[]> ProcessStream(IEnumerable<AudioData> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        PreprocessorStream? stream = null;
        foreach (var piece in pieces)
        {
            stream ??= CreateStream(piece.SampleRate, piece.Channels);
            var output = stream.Process(piece.Samples);
            if (output.Length > 0) yield return output;
        }
        if (stream is null) yield break;
        var rest = stream.Complete();
        if (rest.Length > 0) yield return rest;
    }

    /// <summary>Averages all channels (same as librosa.to_mono).</summary>
    public static float[] ToMono(float[] interleaved, int channels)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        if (channels == 1) return (float[])interleaved.Clone();

        int frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
            mono[i] = (float)(sum / channels);
        }
        return mono;
    }

    /// <summary>Band-limited resampling with a Kaiser-windowed sinc kernel.</summary>
    public static float[] Resample(float[] input, int inputRate, int outputRate)
    {
        var resampler = new StreamingResampler(inputRate, outputRate);
        return [.. resampler.Process(input), .. resampler.Flush()];
    }

    /// <summary>
    /// Silences 20 ms blocks quieter than <paramref name="thresholdDb"/>. The gate opens one block early
    /// (so the attack is kept), stays open for <paramref name="holdSeconds"/>, and fades over 5 ms
    /// so that the switching itself does not create clicks (which would be detected as onsets).
    /// </summary>
    public static float[] ApplyNoiseGate(float[] input, int sampleRate, double thresholdDb, double holdSeconds)
    {
        var gate = new NoiseGate(sampleRate, thresholdDb, holdSeconds);
        return [.. gate.Process(input), .. gate.Flush()];
    }
}

/// <summary>Stage 2 fed piece by piece: mono → resample → noise gate, each stage streaming.</summary>
public sealed class PreprocessorStream
{
    private readonly int _channels;
    private readonly StreamingResampler _resampler;
    private readonly NoiseGate? _gate;

    internal PreprocessorStream(int sampleRate, int channels, int targetRate, NoiseGate? gate)
    {
        if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
        _channels = channels;
        _resampler = new StreamingResampler(sampleRate, targetRate);
        _gate = gate;
    }

    /// <summary>Adds interleaved samples (whole frames) and returns the output samples that are now final.</summary>
    public float[] Process(float[] interleaved)
    {
        if (interleaved.Length % _channels != 0)
            throw new ArgumentException("Pieces must contain whole frames.", nameof(interleaved));
        var resampled = _resampler.Process(AudioPreprocessor.ToMono(interleaved, _channels));
        return _gate is null ? resampled : _gate.Process(resampled);
    }

    /// <summary>Signals the end of the input and returns the remaining output.</summary>
    public float[] Complete()
    {
        var rest = _resampler.Flush();
        return _gate is null ? rest : [.. _gate.Process(rest), .. _gate.Flush()];
    }
}
