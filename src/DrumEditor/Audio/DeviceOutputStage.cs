namespace DrumEditor.Audio;

/// <summary>Linear device gain before the emergency limiter. Shared sources remain unchanged.</summary>
public sealed class DeviceOutputStage(int sampleRate, float initialVolume = .5f)
{
    public const float DefaultVolume = .5f;
    readonly StereoPeakLimiter limiter = new(sampleRate);
    float currentVolume = float.IsFinite(initialVolume) && initialVolume is >=0 and <=1 ? initialVolume : throw new ArgumentOutOfRangeException(nameof(initialVolume));
    readonly float volumeStep = 1f / Math.Max(1, sampleRate * .02f);

    public void Process(float[] samples, int frames, float volume)
    {
        if (frames < 0 || frames > samples.Length / 2) throw new ArgumentOutOfRangeException(nameof(frames));
        if (!float.IsFinite(volume) || volume < 0 || volume > 1) throw new ArgumentOutOfRangeException(nameof(volume));
        for (int frame = 0; frame < frames; frame++)
        {
            currentVolume += Math.Clamp(volume - currentVolume, -volumeStep, volumeStep);
            samples[frame * 2] *= currentVolume;
            samples[frame * 2 + 1] *= currentVolume;
        }
        limiter.Process(samples, frames);
    }
}
