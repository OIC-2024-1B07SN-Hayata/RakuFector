namespace DrumEditor.Audio;

/// <summary>Final device-output protection. Leaves the shared additive source linear.</summary>
public sealed class StereoPeakLimiter
{
    public const float Ceiling = .95f;
    readonly double release;
    double gain = 1;

    public StereoPeakLimiter(int sampleRate)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        release = Math.Exp(-1d / (sampleRate * .1));
    }

    public void Process(float[] samples, int frames)
    {
        if (frames < 0 || frames > samples.Length / 2) throw new ArgumentOutOfRangeException(nameof(frames));
        for (int frame = 0; frame < frames; frame++)
        {
            int i = frame * 2;
            float left = samples[i], right = samples[i + 1];
            if (!float.IsFinite(left) || !float.IsFinite(right))
            {
                samples[i] = samples[i + 1] = 0;
                continue;
            }
            double peak = Math.Max(Math.Abs((double)left), Math.Abs((double)right));
            double required = peak > Ceiling ? Ceiling / peak : 1;
            gain = Math.Min(required, 1 - (1 - gain) * release);
            samples[i] = (float)(left * gain);
            samples[i + 1] = (float)(right * gain);
        }
    }
}
