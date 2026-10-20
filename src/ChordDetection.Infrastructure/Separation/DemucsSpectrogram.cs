namespace ChordDetection.Infrastructure.Separation;

/// <summary>
/// HTDemucs._spec / _ispec (torch.stft / istft with n_fft 4096, hop 1024, periodic Hann window,
/// normalized=True, center=True, reflect padding). Verified against demucs with tools/demucs_onnx.py.
/// </summary>
internal sealed class DemucsSpectrogram
{
    public const int Nfft = 4096;
    public const int Hop = Nfft / 4;
    public const int Bins = Nfft / 2;            // Nyquist bin is dropped
    private const int Pad = Hop / 2 * 3;          // 1536

    private readonly Fft _fft = new(Nfft);
    private readonly double[] _window = new double[Nfft];
    private readonly double _scale = 1 / Math.Sqrt(Nfft);

    public DemucsSpectrogram()
    {
        for (int i = 0; i < Nfft; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / Nfft);
    }

    public static int Frames(int length) => (length + Hop - 1) / Hop;

    /// <summary>
    /// STFT of one channel → real and imaginary parts, [Bins, Frames(length)] row-major (frequency-major).
    /// </summary>
    public (double[] Re, double[] Im) Forward(ReadOnlySpan<float> signal)
    {
        int length = signal.Length;
        int frames = Frames(length);

        // _spec pads (Pad, Pad + frames*Hop - length) with reflection, then torch.stft(center=True) pads Nfft/2 more.
        var inner = Reflect(signal, Pad, Pad + frames * Hop - length);
        var padded = Reflect(inner, Nfft / 2, Nfft / 2);

        var re = new double[Bins * frames];
        var im = new double[Bins * frames];
        var bufRe = new double[Nfft];
        var bufIm = new double[Nfft];
        for (int t = 0; t < frames; t++)
        {
            int offset = (t + 2) * Hop;           // _spec keeps frames 2 .. 2 + frames
            for (int i = 0; i < Nfft; i++)
            {
                bufRe[i] = padded[offset + i] * _window[i];
                bufIm[i] = 0;
            }
            _fft.Transform(bufRe, bufIm, inverse: false);
            for (int f = 0; f < Bins; f++)
            {
                re[f * frames + t] = bufRe[f] * _scale;
                im[f * frames + t] = bufIm[f] * _scale;
            }
        }
        return (re, im);
    }

    /// <summary>
    /// Inverse of <see cref="Forward"/> for one channel. <paramref name="re"/>/<paramref name="im"/> are read as
    /// [Bins, frames] at the given offset with the given frame count (frames = Frames(length)).
    /// </summary>
    public double[] Inverse(ReadOnlySpan<float> re, ReadOnlySpan<float> im, int frames, int length)
    {
        // _ispec: zero Nyquist bin, two zero frames on each side, istft(center=True), then cut Pad.
        int totalFrames = frames + 4;
        int full = Nfft + Hop * (totalFrames - 1);
        var y = new double[full];
        var env = new double[full];
        var bufRe = new double[Nfft];
        var bufIm = new double[Nfft];
        double gain = Math.Sqrt(Nfft) / Nfft;     // normalized=True, and irfft's 1/N

        for (int t = 0; t < totalFrames; t++)
        {
            int start = t * Hop;
            for (int i = 0; i < Nfft; i++) env[start + i] += _window[i] * _window[i];

            int src = t - 2;
            if (src < 0 || src >= frames) continue; // zero frame

            // Hermitian spectrum for irfft; the imaginary parts of DC and Nyquist are ignored (as numpy/torch do).
            bufRe[0] = re[src];
            bufIm[0] = 0;
            for (int f = 1; f < Bins; f++)
            {
                bufRe[f] = re[f * frames + src];
                bufIm[f] = im[f * frames + src];
                bufRe[Nfft - f] = bufRe[f];
                bufIm[Nfft - f] = -bufIm[f];
            }
            bufRe[Bins] = 0;
            bufIm[Bins] = 0;
            _fft.Transform(bufRe, bufIm, inverse: true);
            for (int i = 0; i < Nfft; i++) y[start + i] += bufRe[i] * gain * _window[i];
        }

        var output = new double[length];
        int first = Nfft / 2 + Pad;
        for (int i = 0; i < length; i++) output[i] = y[first + i] / env[first + i];
        return output;
    }

    /// <summary>numpy/torch "reflect" padding (the edge sample is not repeated).</summary>
    internal static double[] Reflect(ReadOnlySpan<float> x, int left, int right)
    {
        var data = new double[x.Length];
        for (int i = 0; i < x.Length; i++) data[i] = x[i];
        return Reflect(data, left, right);
    }

    internal static double[] Reflect(double[] x, int left, int right)
    {
        int n = x.Length;
        if (left >= n || right >= n) throw new ArgumentException("Signal too short for reflect padding.");
        var y = new double[n + left + right];
        for (int i = 0; i < y.Length; i++)
        {
            int j = i - left;
            if (j < 0) j = -j;
            else if (j >= n) j = 2 * (n - 1) - j;
            y[i] = x[j];
        }
        return y;
    }
}
