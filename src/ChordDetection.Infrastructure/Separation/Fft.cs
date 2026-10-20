namespace ChordDetection.Infrastructure.Separation;

/// <summary>Iterative radix-2 complex FFT (size must be a power of two). Not normalised in either direction.</summary>
internal sealed class Fft
{
    private readonly int _size;
    private readonly int[] _bitReverse;
    private readonly double[] _cos;
    private readonly double[] _sin;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0) throw new ArgumentException("Size must be a power of two.", nameof(size));
        _size = size;
        int bits = (int)Math.Log2(size);
        _bitReverse = new int[size];
        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            _bitReverse[i] = r;
        }
        _cos = new double[size / 2];
        _sin = new double[size / 2];
        for (int i = 0; i < size / 2; i++)
        {
            _cos[i] = Math.Cos(2 * Math.PI * i / size);
            _sin[i] = Math.Sin(2 * Math.PI * i / size);
        }
    }

    public int Size => _size;

    /// <summary>In place. Forward: X[k] = Σ x[n]·e^(−2πikn/N); inverse uses e^(+…) and no 1/N.</summary>
    public void Transform(double[] re, double[] im, bool inverse)
    {
        for (int i = 0; i < _size; i++)
        {
            int j = _bitReverse[i];
            if (j <= i) continue;
            (re[i], re[j]) = (re[j], re[i]);
            (im[i], im[j]) = (im[j], im[i]);
        }

        double sign = inverse ? 1 : -1;
        for (int len = 2; len <= _size; len <<= 1)
        {
            int half = len / 2, step = _size / len;
            for (int start = 0; start < _size; start += len)
            {
                for (int k = 0; k < half; k++)
                {
                    double wr = _cos[k * step], wi = sign * _sin[k * step];
                    int a = start + k, b = a + half;
                    double tr = re[b] * wr - im[b] * wi;
                    double ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
        }
    }
}
