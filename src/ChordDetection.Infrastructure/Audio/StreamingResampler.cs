namespace ChordDetection.Infrastructure.Audio;

/// <summary>
/// Band-limited resampling with a Kaiser-windowed sinc kernel, fed piece by piece.
/// An output sample is produced as soon as every input sample its kernel reads is available,
/// so the concatenated output is identical to resampling the whole signal at once.
/// </summary>
internal sealed class StreamingResampler
{
    // Half-width of the sinc kernel in zero-crossings of the (lower) cutoff.
    private const int KernelZeroCrossings = 32;
    private const double KaiserBeta = 8.6;
    // Cutoff relative to the lower Nyquist frequency, leaves room for the transition band.
    private const double Rolloff = 0.945;
    // Kernel table entries per input sample.
    private const int TableResolution = 512;

    private readonly bool _passThrough;
    private readonly double _ratio;
    private readonly double _halfWidth;
    private readonly double[] _table = [];
    private readonly FloatBuffer _input = new();
    private long _nextOutput;

    public StreamingResampler(int inputRate, int outputRate)
    {
        if (inputRate <= 0 || outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
        _passThrough = inputRate == outputRate;
        if (_passThrough) return;

        _ratio = (double)outputRate / inputRate;
        // When downsampling, the cutoff must follow the output Nyquist frequency (anti-aliasing).
        double cutoff = Math.Min(1.0, _ratio) * Rolloff;          // relative to input Nyquist
        _halfWidth = KernelZeroCrossings / cutoff;                 // in input samples
        _table = BuildKernelTable(cutoff, _halfWidth);
    }

    /// <summary>Adds input samples and returns the output samples that are now final.</summary>
    public float[] Process(ReadOnlySpan<float> input)
    {
        if (_passThrough) return input.ToArray();
        _input.Append(input);

        // Output n reads input up to floor(n / ratio + halfWidth); "ready" = number of outputs whose inputs are all here.
        int last = _input.Count - 1;
        long ready = Math.Max(0, (long)Math.Floor((last - _halfWidth) * _ratio));
        while (ready > 0 && LastInput(ready - 1) > last) ready--;
        while (LastInput(ready) <= last) ready++;
        return Produce(Math.Max(_nextOutput, ready));
    }

    /// <summary>Signals the end of the input and returns the remaining output samples.</summary>
    public float[] Flush()
    {
        if (_passThrough) return [];
        long total = (long)Math.Ceiling(_input.Count * _ratio);
        return Produce(total);
    }

    private double LastInput(long n) => Math.Floor(n / _ratio + _halfWidth);

    private float[] Produce(long end)
    {
        int count = (int)(end - _nextOutput);
        if (count <= 0) return [];

        var output = new float[count];
        long first = _nextOutput;
        var input = _input.RawArray;
        int length = _input.Count;
        Parallel.For(0, count, j =>
        {
            long n = first + j;
            double center = n / _ratio; // position in input samples
            int from = Math.Max(0, (int)Math.Ceiling(center - _halfWidth));
            int to = Math.Min(length - 1, (int)Math.Floor(center + _halfWidth));
            double acc = 0;
            for (int k = from; k <= to; k++)
            {
                acc += input[k] * Lookup(_table, Math.Abs(k - center));
            }
            output[j] = (float)acc;
        });
        _nextOutput = end;
        return output;
    }

    // Kernel table: cutoff * sinc(cutoff * x) * kaiser(x / halfWidth), x = 0..halfWidth.
    private static double[] BuildKernelTable(double cutoff, double halfWidth)
    {
        double i0Beta = BesselI0(KaiserBeta);
        int size = (int)Math.Ceiling(halfWidth * TableResolution) + 2;
        var table = new double[size];
        for (int i = 0; i < size; i++)
        {
            double x = (double)i / TableResolution;
            table[i] = x > halfWidth ? 0 : cutoff * Sinc(cutoff * x) * Kaiser(x / halfWidth, i0Beta);
        }
        return table;
    }

    private static double Lookup(double[] table, double x)
    {
        double pos = x * TableResolution;
        int i = (int)pos;
        if (i >= table.Length - 1) return 0;
        double frac = pos - i;
        return table[i] + (table[i + 1] - table[i]) * frac;
    }

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-12) return 1.0;
        double px = Math.PI * x;
        return Math.Sin(px) / px;
    }

    /// <param name="r">Position relative to the half width (-1..1).</param>
    private static double Kaiser(double r, double i0Beta)
    {
        double t = 1 - r * r;
        return t <= 0 ? 0 : BesselI0(KaiserBeta * Math.Sqrt(t)) / i0Beta;
    }

    /// <summary>Modified Bessel function of the first kind, order 0 (series expansion).</summary>
    private static double BesselI0(double x)
    {
        double sum = 1, term = 1, half = x / 2;
        for (int k = 1; k < 50; k++)
        {
            term *= half / k;
            double t2 = term * term;
            sum += t2;
            if (t2 < sum * 1e-17) break;
        }
        return sum;
    }
}
