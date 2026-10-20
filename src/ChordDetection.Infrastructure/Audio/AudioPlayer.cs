using NAudio.Wave;

namespace ChordDetection.Infrastructure.Audio;

/// <summary>
/// Plays loaded audio (the file that was analysed) through the default output device.
/// Create and call it on the UI thread: <see cref="PlaybackEnded"/> is raised on the thread that started playback.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    // Small enough that the timeline playhead follows the sound closely.
    private const int LatencyMilliseconds = 100;

    private MemorySampleProvider? _source;
    private WaveOutEvent? _output;
    private long _outputStartFrame;

    /// <summary>Raised when playback reaches the end (not on <see cref="Stop"/> or <see cref="Seek"/>).</summary>
    public event EventHandler? PlaybackEnded;

    public bool IsLoaded => _source is not null;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public double DurationSeconds => _source is null ? 0 : (double)_source.FrameCount / _source.WaveFormat.SampleRate;

    /// <summary>Position of the sound being heard (the output buffer is not counted).</summary>
    public double PositionSeconds
    {
        get
        {
            if (_source is null) return 0;
            long frame = _output is null
                ? _source.Frame
                : _outputStartFrame + _output.GetPosition() / _source.WaveFormat.BlockAlign;
            return (double)Math.Min(frame, _source.FrameCount) / _source.WaveFormat.SampleRate;
        }
    }

    /// <summary>Replaces the audio to play (stops current playback). More than 2 channels are mixed down to mono.</summary>
    public void Load(AudioData audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        Unload();
        var playable = audio.Channels <= 2 ? audio : ToMono(audio);
        _source = new MemorySampleProvider(playable.Samples,
            WaveFormat.CreateIeeeFloatWaveFormat(playable.SampleRate, playable.Channels));
    }

    public void Unload()
    {
        ReleaseOutput();
        _source = null;
    }

    public void Play()
    {
        if (_source is null || IsPlaying) return;
        if (_output is null)
        {
            if (_source.Frame >= _source.FrameCount) _source.Frame = 0;
            _output = new WaveOutEvent { DesiredLatency = LatencyMilliseconds };
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_source);
            _outputStartFrame = _source.Frame;
        }
        _output.Play();
    }

    public void Pause() => _output?.Pause();

    /// <summary>Stops and rewinds to the beginning.</summary>
    public void Stop()
    {
        ReleaseOutput();
        if (_source is not null) _source.Frame = 0;
    }

    /// <summary>Moves to <paramref name="seconds"/>; keeps playing if it was playing.</summary>
    public void Seek(double seconds)
    {
        if (_source is null) return;
        bool wasPlaying = IsPlaying;
        ReleaseOutput();
        long frame = (long)(Math.Max(0, seconds) * _source.WaveFormat.SampleRate);
        _source.Frame = Math.Min(frame, _source.FrameCount);
        if (wasPlaying) Play();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Only natural ends get here: ReleaseOutput unsubscribes before stopping.
        ReleaseOutput();
        if (_source is not null) _source.Frame = 0;
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    // The device is reopened from the source position on the next Play; this keeps the position bookkeeping simple.
    private void ReleaseOutput()
    {
        if (_output is null) return;
        _output.PlaybackStopped -= OnPlaybackStopped;
        _output.Dispose();
        _output = null;
    }

    private static AudioData ToMono(AudioData audio)
    {
        var mono = new float[audio.FrameCount];
        for (int frame = 0; frame < mono.Length; frame++)
        {
            float sum = 0;
            for (int ch = 0; ch < audio.Channels; ch++) sum += audio.Samples[frame * audio.Channels + ch];
            mono[frame] = sum / audio.Channels;
        }
        return new AudioData(mono, audio.SampleRate, 1);
    }

    public void Dispose() => Unload();

    /// <summary>Interleaved float samples in memory, read by the output device from its own thread.</summary>
    private sealed class MemorySampleProvider(float[] samples, WaveFormat format) : ISampleProvider
    {
        private long _sample;

        public WaveFormat WaveFormat { get; } = format;

        public long FrameCount => samples.Length / WaveFormat.Channels;

        public long Frame
        {
            get => Interlocked.Read(ref _sample) / WaveFormat.Channels;
            set => Interlocked.Exchange(ref _sample, value * WaveFormat.Channels);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            long start = Interlocked.Read(ref _sample);
            int n = (int)Math.Clamp(samples.Length - start, 0, count);
            // Not Array.Copy: NAudio passes a byte[] disguised as float[] (WaveBuffer), which Array.Copy rejects.
            for (int i = 0; i < n; i++) buffer[offset + i] = samples[start + i];
            Interlocked.Exchange(ref _sample, start + n);
            return n; // 0 at the end → the device stops and raises PlaybackStopped
        }
    }
}
