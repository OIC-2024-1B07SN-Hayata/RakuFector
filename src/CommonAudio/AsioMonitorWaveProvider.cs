using System;
using System.Threading;
using NAudio.Wave;

namespace CommonAudio;

/// <summary>
/// 同じASIOコールバックで取得したモノラル音声を、左右へそのまま送ります。
/// NAudio 2.2.1はAudioAvailableの通知後にReadを呼びます。
/// </summary>
internal sealed class AsioMonitorWaveProvider : IWaveProvider
{
    private float[] _stereo = Array.Empty<float>();
    private bool _hasInput;
    private long _readCount;
    private string? _lastError;
    public long ReadCount => Interlocked.Read(ref _readCount);
    public string? LastError => Volatile.Read(ref _lastError);

    public WaveFormat WaveFormat { get; }

    public AsioMonitorWaveProvider(int sampleRate) =>
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

    public void Prepare(int framesPerBuffer)
    {
        _stereo = new float[checked(framesPerBuffer * 2)];
        _hasInput = false;
    }

    // 受信側で共有する元の配列は変更せず、出力用の領域へコピーします。
    public void SetInput(float[] mono)
    {
        if (mono.Length * 2 != _stereo.Length)
            throw new InvalidOperationException("出力バッファサイズが変わりました。再接続してください。");

        for (int i = 0; i < mono.Length; i++)
        {
            float sample = float.IsFinite(mono[i]) ? mono[i] : 0;
            _stereo[i * 2] = sample;
            _stereo[i * 2 + 1] = sample;
        }
        _hasInput = true;
    }

    public void Clear() => _hasInput = false;

    public int Read(byte[] buffer, int offset, int count)
    {
        // このProviderはAsioOut専用で、毎回1ブロックを読み出します。
        Array.Clear(buffer, offset, count);
        if (_hasInput)
        {
            if (count == _stereo.Length * sizeof(float))
            {
                Buffer.BlockCopy(_stereo, 0, buffer, offset, count);
                Interlocked.Increment(ref _readCount);
            }
            else Volatile.Write(ref _lastError, "出力要求サイズが一致しません。再接続してください。");
        }
        _hasInput = false;
        // 無音でも要求されたサイズを返し、出力を継続します。
        return count;
    }
}
