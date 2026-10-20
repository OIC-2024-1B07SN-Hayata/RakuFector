namespace ChordDetection.Infrastructure.Audio;

/// <summary>Raw audio as read from a file or recorder.</summary>
/// <param name="Samples">Interleaved float samples (-1..1).</param>
/// <param name="SampleRate">Samples per second per channel.</param>
/// <param name="Channels">Number of interleaved channels.</param>
public sealed record AudioData(float[] Samples, int SampleRate, int Channels)
{
    public int FrameCount => Channels == 0 ? 0 : Samples.Length / Channels;

    public double DurationSeconds => SampleRate == 0 ? 0 : (double)FrameCount / SampleRate;

    /// <summary>Splits into consecutive pieces of about <paramref name="seconds"/> (for streaming analysis).</summary>
    public IEnumerable<AudioData> Split(double seconds)
    {
        if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        int framesPerPiece = Math.Max(1, (int)(seconds * SampleRate));
        for (int frame = 0; frame < FrameCount; frame += framesPerPiece)
        {
            int frames = Math.Min(framesPerPiece, FrameCount - frame);
            yield return new AudioData(Samples.AsSpan(frame * Channels, frames * Channels).ToArray(), SampleRate, Channels);
        }
    }
}
