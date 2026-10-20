using System;
using System.Buffers.Binary;
using System.Threading;
using NAudio.Wave;

namespace CommonAudio;

/// <summary>ASIO入力とPC出力の別スレッド・別バッファをつなぐ、上限付きモノラルキュー。</summary>
public sealed class PcMonitorWaveProvider : IWaveProvider
{
    private readonly object _gate = new();
    private readonly float[] _ring;
    private int _head, _count;
    private long _readCount, _droppedFrames;
    public WaveFormat WaveFormat { get; }
    public long ReadCount => Interlocked.Read(ref _readCount);
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);
    public int BufferedFrames { get { lock (_gate) return _count; } }
    public int MaximumBufferedFrames => _ring.Length;

    public PcMonitorWaveProvider(int sampleRate, int maximumBufferedFrames = 0)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (maximumBufferedFrames < 0) throw new ArgumentOutOfRangeException(nameof(maximumBufferedFrames));
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        _ring = new float[maximumBufferedFrames == 0 ? Math.Max(1, sampleRate / 50) : maximumBufferedFrames];
    }

    public void SetInput(float[] samples)
    {
        if (samples is null) throw new ArgumentNullException(nameof(samples));
        lock (_gate)
        {
            int start = Math.Max(0, samples.Length - _ring.Length);
            int incoming = samples.Length - start;
            int discard = Math.Max(0, _count + incoming - _ring.Length);
            _head = (_head + discard) % _ring.Length;
            _count -= discard;
            Interlocked.Add(ref _droppedFrames, discard + start);
            for (int i = start; i < samples.Length; i++)
            {
                float value = samples[i];
                _ring[(_head + _count) % _ring.Length] = float.IsFinite(value) ? Math.Clamp(value, -1, 1) : 0;
                _count++;
            }
        }
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset > buffer.Length - count)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (count % WaveFormat.BlockAlign != 0)
            throw new ArgumentException("出力はステレオフレーム単位で読み取ってください。", nameof(count));
        Array.Clear(buffer, offset, count); // 入力待ちでは無音を返し、再生を継続。
        lock (_gate)
        {
            int frames = Math.Min(count / WaveFormat.BlockAlign, _count);
            for (int i = 0; i < frames; i++)
            {
                float value = _ring[_head];
                _head = (_head + 1) % _ring.Length;
                int at = offset + i * WaveFormat.BlockAlign;
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(at, 4), value);
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(at + 4, 4), value);
            }
            _count -= frames;
        }
        if (count > 0) Interlocked.Increment(ref _readCount);
        return count;
    }

    public void Clear()
    {
        lock (_gate) { _head = 0; _count = 0; }
    }
}
