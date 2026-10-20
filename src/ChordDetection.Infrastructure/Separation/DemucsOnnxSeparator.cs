using ChordDetection.Infrastructure.Audio;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ChordDetection.Infrastructure.Separation;

/// <summary>
/// Demucs (htdemucs_6s) source separation with ONNX Runtime, no Python needed.
///
/// The ONNX file (made by tools/demucs_onnx.py) holds HTDemucs between its STFT and ISTFT;
/// this class does the rest exactly like demucs.apply.apply_model(shifts=0, split=True, overlap=0.25):
///  1. normalise the whole mix by the mean / std of its mono sum,
///  2. cut 7.8 s chunks every 5.85 s (neighbouring samples as context, zeros outside the song),
///  3. per chunk: STFT → model → ISTFT(spec) + wave,
///  4. cross-fade the chunks with triangular weights and undo the normalisation.
/// A part of the output is final once the next chunk no longer overlaps it, so it is yielded right away.
/// </summary>
public sealed class DemucsOnnxSeparator : ISourceSeparator
{
    public const int SampleRate = 44100;
    private const int Channels = 2;
    private const double Overlap = 0.25;

    private readonly string _modelPath;
    private InferenceSession? _session;
    private string[] _sources = [];
    private int _segment;

    public DemucsOnnxSeparator(string? modelPath = null)
    {
        _modelPath = modelPath ?? DefaultModelPath;
    }

    /// <summary>Models\htdemucs_6s.onnx next to the executable.</summary>
    public static string DefaultModelPath => Path.Combine(AppContext.BaseDirectory, "Models", "htdemucs_6s.onnx");

    public static bool IsModelAvailable(string? modelPath = null) => File.Exists(modelPath ?? DefaultModelPath);

    public IReadOnlyList<string> Sources
    {
        get
        {
            EnsureSession();
            return _sources;
        }
    }

    public IEnumerable<AudioData> Separate(AudioData mix, string source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mix);
        EnsureSession();
        int sourceIndex = Array.IndexOf(_sources, source);
        if (sourceIndex < 0)
            throw new ArgumentException($"Unknown source '{source}' (available: {string.Join(", ", _sources)}).", nameof(source));
        return SeparateCore(ToStereo44k(mix), sourceIndex, cancellationToken);
    }

    private IEnumerable<AudioData> SeparateCore(float[][] mix, int sourceIndex, CancellationToken ct)
    {
        int length = mix[0].Length;
        if (length == 0) yield break;

        // 1. Normalise by the mono sum (demucs.separate does this before apply_model).
        var (mean, std) = MeanStd(mix);
        var normalized = mix.Select(ch => ch.Select(v => (float)((v - mean) / std)).ToArray()).ToArray();

        int segment = _segment;
        int stride = (int)((1 - Overlap) * segment);
        var weight = TransitionWeights(segment);
        var output = new double[Channels][];
        for (int c = 0; c < Channels; c++) output[c] = new double[length];
        var sumWeight = new double[length];
        var spectrogram = new DemucsSpectrogram();
        int emitted = 0;

        for (int offset = 0; offset < length; offset += stride)
        {
            ct.ThrowIfCancellationRequested();

            // 2. TensorChunk.padded(segment): centre the chunk, take real neighbours, zeros outside.
            int chunkLength = Math.Min(segment, length - offset);
            int delta = segment - chunkLength;
            int start = offset - delta / 2;
            var chunk = new float[Channels][];
            for (int c = 0; c < Channels; c++)
            {
                chunk[c] = new float[segment];
                int from = Math.Max(0, start), to = Math.Min(length, start + segment);
                Array.Copy(normalized[c], from, chunk[c], from - start, to - from);
            }

            // 3. Model + ISTFT for the requested source only.
            var separated = RunChunk(spectrogram, chunk, sourceIndex);

            // 4. Weighted overlap-add (center_trim by delta / 2).
            for (int c = 0; c < Channels; c++)
            {
                for (int i = 0; i < chunkLength; i++)
                    output[c][offset + i] += weight[i] * separated[c][delta / 2 + i];
            }
            for (int i = 0; i < chunkLength; i++) sumWeight[offset + i] += weight[i];

            int final = offset + stride >= length ? length : offset + stride;
            if (final > emitted)
            {
                yield return Finish(output, sumWeight, emitted, final, mean, std);
                emitted = final;
            }
        }
    }

    private float[][] RunChunk(DemucsSpectrogram spectrogram, float[][] chunk, int sourceIndex)
    {
        int segment = chunk[0].Length;
        int frames = DemucsSpectrogram.Frames(segment);
        int plane = DemucsSpectrogram.Bins * frames;

        // mag: [1, 4, Bins, frames], channels = re0, im0, re1, im1 (complex-as-channels).
        var mag = new float[4 * plane];
        var mixInput = new float[Channels * segment];
        for (int c = 0; c < Channels; c++)
        {
            var (re, im) = spectrogram.Forward(chunk[c]);
            for (int i = 0; i < plane; i++)
            {
                mag[(2 * c) * plane + i] = (float)re[i];
                mag[(2 * c + 1) * plane + i] = (float)im[i];
            }
            Array.Copy(chunk[c], 0, mixInput, c * segment, segment);
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("mix", new DenseTensor<float>(mixInput, [1, Channels, segment])),
            NamedOnnxValue.CreateFromTensor("mag", new DenseTensor<float>(mag, [1, 4, DemucsSpectrogram.Bins, frames])),
        };
        using var results = _session!.Run(inputs);
        var spec = ((DenseTensor<float>)results.First(r => r.Name == "spec").AsTensor<float>()).Buffer.Span;
        var wave = ((DenseTensor<float>)results.First(r => r.Name == "wave").AsTensor<float>()).Buffer.Span;

        var result = new float[Channels][];
        for (int c = 0; c < Channels; c++)
        {
            int specBase = (sourceIndex * 4 + 2 * c) * plane;
            var fromSpec = spectrogram.Inverse(spec.Slice(specBase, plane), spec.Slice(specBase + plane, plane), frames, segment);
            int waveBase = (sourceIndex * Channels + c) * segment;
            result[c] = new float[segment];
            for (int i = 0; i < segment; i++) result[c][i] = (float)(fromSpec[i] + wave[waveBase + i]);
        }
        return result;
    }

    private static AudioData Finish(double[][] output, double[] sumWeight, int from, int to, double mean, double std)
    {
        var samples = new float[(to - from) * Channels];
        for (int i = from; i < to; i++)
        {
            for (int c = 0; c < Channels; c++)
                samples[(i - from) * Channels + c] = (float)(output[c][i] / sumWeight[i] * std + mean);
        }
        return new AudioData(samples, SampleRate, Channels);
    }

    /// <summary>apply_model's weight: 1..N/2 then N−N/2..1, divided by its maximum.</summary>
    private static double[] TransitionWeights(int segment)
    {
        var w = new double[segment];
        int half = segment / 2;
        for (int i = 0; i < half; i++) w[i] = i + 1;
        for (int i = half; i < segment; i++) w[i] = segment - i;
        double max = w.Max();
        for (int i = 0; i < segment; i++) w[i] /= max;
        return w;
    }

    /// <summary>Mean and sample standard deviation (N − 1, like torch.std) of the channel average.</summary>
    private static (double Mean, double Std) MeanStd(float[][] mix)
    {
        int n = mix[0].Length;
        double sum = 0;
        var mono = new double[n];
        for (int i = 0; i < n; i++)
        {
            mono[i] = (mix[0][i] + (double)mix[1][i]) / 2;
            sum += mono[i];
        }
        double mean = sum / n;
        double sq = 0;
        for (int i = 0; i < n; i++) sq += (mono[i] - mean) * (mono[i] - mean);
        double std = n > 1 ? Math.Sqrt(sq / (n - 1)) : 0;
        return (mean, std > 0 ? std : 1);
    }

    /// <summary>demucs convert_audio: mono is duplicated, more channels are cut to two, then resampled to 44.1 kHz.</summary>
    private static float[][] ToStereo44k(AudioData audio)
    {
        int frames = audio.FrameCount;
        var channels = new float[Channels][];
        for (int c = 0; c < Channels; c++)
        {
            int src = Math.Min(c, audio.Channels - 1);
            var ch = new float[frames];
            for (int i = 0; i < frames; i++) ch[i] = audio.Samples[i * audio.Channels + src];
            channels[c] = audio.SampleRate == SampleRate ? ch : AudioPreprocessor.Resample(ch, audio.SampleRate, SampleRate);
        }
        return channels;
    }

    private void EnsureSession()
    {
        if (_session is not null) return;
        if (!File.Exists(_modelPath))
            throw new FileNotFoundException(
                "Demucs の ONNX モデルが見つかりません。tools/demucs_onnx.py export で作成し、Models フォルダに置いてください。", _modelPath);

        var session = new InferenceSession(_modelPath, new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL });
        var meta = session.ModelMetadata.CustomMetadataMap;
        _sources = meta["sources"].Split(',');
        _segment = int.Parse(meta["segment_samples"], System.Globalization.CultureInfo.InvariantCulture);
        if (int.Parse(meta["samplerate"], System.Globalization.CultureInfo.InvariantCulture) != SampleRate)
            throw new InvalidOperationException("Unexpected Demucs sample rate.");
        _session = session;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
}
