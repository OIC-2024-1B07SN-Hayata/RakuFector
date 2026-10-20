namespace ChordDetection.Infrastructure.Audio;

/// <summary>
/// Silences 20 ms blocks quieter than the threshold. The gate opens one block early (so the attack
/// is kept), stays open for the hold time, and fades over 5 ms so that the switching itself does not
/// create clicks (which would be detected as onsets).
/// Fed piece by piece; a block is released once the next block is known (one block of look-ahead),
/// so the concatenated output is identical to gating the whole signal at once.
/// </summary>
internal sealed class NoiseGate
{
    private const double BlockSeconds = 0.02;
    private const double RampSeconds = 0.005;

    private readonly int _block;
    private readonly double _threshold;
    private readonly int _holdBlocks;
    private readonly double _step;
    private readonly FloatBuffer _pending = new();   // samples from block _nextBlock on
    private readonly List<bool> _loud = [];          // loudness of every completed block
    private int _nextBlock;                           // first block not yet released
    private double _gain = double.NaN;                // NaN until the first block is released

    public NoiseGate(int sampleRate, double thresholdDb, double holdSeconds)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        _block = Math.Max(1, (int)(sampleRate * BlockSeconds));
        _threshold = Math.Pow(10, thresholdDb / 20);
        _holdBlocks = (int)Math.Ceiling(holdSeconds / BlockSeconds);
        _step = 1.0 / Math.Max(1, sampleRate * RampSeconds);
    }

    /// <summary>Adds samples and returns the gated samples that are now final.</summary>
    public float[] Process(ReadOnlySpan<float> input)
    {
        _pending.Append(input);
        MeasureCompleteBlocks(includePartial: false);
        // Block b needs the loudness of block b + 1.
        return Release(_loud.Count - 1);
    }

    /// <summary>Signals the end of the input and returns the remaining samples.</summary>
    public float[] Flush()
    {
        MeasureCompleteBlocks(includePartial: true);
        return Release(_loud.Count);
    }

    private void MeasureCompleteBlocks(bool includePartial)
    {
        while (true)
        {
            int from = (_loud.Count - _nextBlock) * _block; // offset of the next unmeasured block in _pending
            int available = _pending.Count - from;
            if (available <= 0 || (available < _block && !includePartial)) return;

            int length = Math.Min(_block, available);
            double sum = 0;
            for (int i = from; i < from + length; i++) sum += _pending[i] * (double)_pending[i];
            _loud.Add(Math.Sqrt(sum / length) >= _threshold);
        }
    }

    private float[] Release(int blockEnd)
    {
        int blocks = blockEnd - _nextBlock;
        if (blocks <= 0) return [];

        int samples = Math.Min(_pending.Count, blocks * _block);
        var output = _pending.Span[..samples].ToArray();
        for (int i = 0; i < samples; i++)
        {
            int b = _nextBlock + i / _block;
            double target = IsOpen(b) ? 1 : 0;
            if (double.IsNaN(_gain)) _gain = target;
            _gain = _gain < target ? Math.Min(target, _gain + _step) : Math.Max(target, _gain - _step);
            output[i] = (float)(output[i] * _gain);
        }

        _pending.RemoveFront(samples);
        _nextBlock = blockEnd;
        return output;
    }

    /// <summary>Open when a loud block lies within [b - hold, b + 1].</summary>
    private bool IsOpen(int b)
    {
        int from = Math.Max(0, b - _holdBlocks), to = Math.Min(_loud.Count - 1, b + 1);
        for (int k = from; k <= to; k++)
            if (_loud[k]) return true;
        return false;
    }
}
