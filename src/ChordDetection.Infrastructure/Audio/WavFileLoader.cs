using NAudio.Wave;
using System.IO;

namespace ChordDetection.Infrastructure.Audio;

/// <summary>Loads a WAV file (8/16/24/32-bit PCM or 32-bit float) into memory.</summary>
public static class WavFileLoader
{
    public static AudioData Load(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var reader = new WaveFileReader(path);
                var provider = reader.ToSampleProvider();
                int channels = provider.WaveFormat.Channels;
                int sampleRate = provider.WaveFormat.SampleRate;

                var samples = new List<float>((int)Math.Min(int.MaxValue, reader.SampleCount * channels));
                var buffer = new float[sampleRate * channels];
                int read;
                while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
                {
                    samples.AddRange(buffer.AsSpan(0, read).ToArray());
                }
                return new AudioData(samples.ToArray(), sampleRate, channels);
            }
            catch (FormatException) when (OperatingSystem.IsWindows())
            {
                // WavファイルとしてのRIFFヘッダーがない場合はMedia Foundationでフォールバック
                return MediaFoundationFileLoader.Load(path);
            }
        }

        throw new InvalidOperationException("Invalid file format, only '.wav' files are supported.");
    }
}
