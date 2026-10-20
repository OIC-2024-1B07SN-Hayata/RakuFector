using System.Runtime.Versioning;
using NAudio.Wave;

namespace ChordDetection.Infrastructure.Audio;

/// <summary>
/// Loads compressed audio (MP3, M4A/AAC, WMA, FLAC …) with Windows Media Foundation.
/// The codecs are part of Windows, so no extra decoder is shipped. Windows only.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MediaFoundationFileLoader
{
    public static AudioData Load(string path)
    {
        // MediaFoundationReader starts Media Foundation itself and decodes to PCM.
        using var reader = new MediaFoundationReader(path);
        var provider = reader.ToSampleProvider();
        int channels = provider.WaveFormat.Channels;
        int sampleRate = provider.WaveFormat.SampleRate;

        var samples = new List<float>();
        var buffer = new float[sampleRate * channels];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read).ToArray());
        }
        return new AudioData(samples.ToArray(), sampleRate, channels);
    }
}
