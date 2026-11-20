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
            // The device counts every frame it played; after the loop end it went back to the loop start.
            var (start, end) = _source.Loop;
            if (_output is not null && end > start && _outputStartFrame < end && frame >= end)
                frame = start + (frame - end) % (end - start);
            return (double)Math.Min(frame, _source.FrameCount) / _source.WaveFormat.SampleRate;
        }
    }

    /// <summary>Shortest loop that is accepted.</summary>
    public const double MinLoopSeconds = 0.05;

    /// <summary>True while a loop is set: playback repeats <see cref="LoopStartSeconds"/>–<see cref="LoopEndSeconds"/> without a gap.</summary>
    public bool IsLooping => _source is not null && _source.Loop.End > _source.Loop.Start;

    public double LoopStartSeconds => IsLooping ? (double)_source!.Loop.Start / _source.WaveFormat.SampleRate : 0;

    public double LoopEndSeconds => IsLooping ? (double)_source!.Loop.End / _source.WaveFormat.SampleRate : 0;

    /// <summary>
    /// Repeats <paramref name="startSeconds"/>–<paramref name="endSeconds"/> (in either order). A position outside the
    /// loop moves to its start; playing continues. Loops shorter than <see cref="MinLoopSeconds"/> are ignored.
    /// </summary>
    public void SetLoop(double startSeconds, double endSeconds)
    {
        if (_source is null) return;
        if (endSeconds < startSeconds) (startSeconds, endSeconds) = (endSeconds, startSeconds);
        int rate = _source.WaveFormat.SampleRate;
        long start = Math.Clamp((long)(Math.Max(0, startSeconds) * rate), 0, _source.FrameCount);
        long end = Math.Clamp((long)(Math.Max(0, endSeconds) * rate), 0, _source.FrameCount);
        if (end - start < (long)(MinLoopSeconds * rate)) return;
        // From the position being heard, not the one the device has read ahead to.
        double position = PositionSeconds;
        _source.Loop = (start, end);
        Seek(position); // reopens the device so that the position bookkeeping starts inside the loop
    }

    /// <summary>Plays on to the end again; the position is kept.</summary>
    public void ClearLoop()
    {
        if (_source is null || !IsLooping) return;
        double position = PositionSeconds;
        _source.Loop = (0, 0);
        Seek(position);
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
            _source.Frame = IntoLoop(_source.Frame);
            _output = new WaveOutEvent { DesiredLatency = LatencyMilliseconds };
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_source);
            _outputStartFrame = _source.Frame;
        }
        _output.Play();
    }

    public void Pause() => _output?.Pause();

    /// <summary>Stops and rewinds to the beginning (to the loop start while looping).</summary>
    public void Stop()
    {
        ReleaseOutput();
        if (_source is not null) _source.Frame = IntoLoop(0);
    }

    /// <summary>Moves to <paramref name="seconds"/>; keeps playing if it was playing.</summary>
    public void Seek(double seconds)
    {
        if (_source is null) return;
        bool wasPlaying = IsPlaying;
        ReleaseOutput();
        long frame = (long)(Math.Max(0, seconds) * _source.WaveFormat.SampleRate);
        _source.Frame = IntoLoop(Math.Min(frame, _source.FrameCount));
        if (wasPlaying) Play();
    }

    /// <summary>While looping, a frame outside the loop is replaced by the loop start.</summary>
    private long IntoLoop(long frame)
    {
        var (start, end) = _source!.Loop;
        return end > start && (frame < start || frame >= end) ? start : frame;
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

        private long _loopStartSample, _loopEndSample; // equal: no loop

        /// <summary>Loop in frames; Start == End means no loop. Read by the output thread.</summary>
        public (long Start, long End) Loop
        {
            get
            {
                lock (this) return (_loopStartSample / WaveFormat.Channels, _loopEndSample / WaveFormat.Channels);
            }
            set
            {
                lock (this)
                {
                    _loopStartSample = value.Start * WaveFormat.Channels;
                    _loopEndSample = value.End * WaveFormat.Channels;
                }
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            long loopStart, loopEnd;
            lock (this) (loopStart, loopEnd) = (_loopStartSample, _loopEndSample);
            bool looping = loopEnd > loopStart;
            long position = Interlocked.Read(ref _sample);
            int written = 0;
            while (written < count)
            {
                if (looping && position >= loopEnd) position = loopStart; // seamless jump back
                long stop = looping && position < loopEnd ? loopEnd : samples.Length;
                int n = (int)Math.Clamp(stop - position, 0, count - written);
                if (n == 0) break;
                // Not Array.Copy: NAudio passes a byte[] disguised as float[] (WaveBuffer), which Array.Copy rejects.
                for (int i = 0; i < n; i++) buffer[offset + written + i] = samples[position + i];
                position += n;
                written += n;
            }
            Interlocked.Exchange(ref _sample, position);
            return written; // 0 at the end → the device stops and raises PlaybackStopped
        }
    }
}
