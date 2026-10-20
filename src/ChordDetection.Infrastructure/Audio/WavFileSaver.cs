using NAudio.Wave;

namespace ChordDetection.Infrastructure.Audio;

public static class WavFileSaver
{
    /// <summary>
    /// Passes the pieces through unchanged while writing them to a float WAV file
    /// (used to keep the separated track so it can be listened to).
    /// </summary>
    public static IEnumerable<AudioData> Tee(IEnumerable<AudioData> pieces, string path)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        WaveFileWriter? writer = null;
        try
        {
            foreach (var piece in pieces)
            {
                writer ??= new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(piece.SampleRate, piece.Channels));
                writer.WriteSamples(piece.Samples, 0, piece.Samples.Length);
                yield return piece;
            }
        }
        finally
        {
            writer?.Dispose();
        }
    }
}
