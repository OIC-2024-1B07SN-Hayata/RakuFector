using NAudio.Wave;

namespace ChordDetection.Infrastructure.Audio;

/// <summary>
/// Stage 1: records the dry guitar signal to a WAV file.
///
/// Design ①: the analysis signal must be taken BEFORE the effect chain, because distortion adds
/// many harmonics that break note detection. Two ways to feed the recorder:
///  - <see cref="StartFromDevice"/>: records an input device directly (used by this demo).
///  - <see cref="StartFromTap"/> + <see cref="WriteTap"/>: the multi-effector calls WriteTap with the
///    buffer it receives at the entrance of the effect chain (a branch of the dry signal).
/// </summary>
public sealed class ChordRecorder : IDisposable
{
    private readonly object _gate = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<string>? _stopped;
    private string? _path;

    /// <summary>Peak level (0..1) of the latest buffer, for a level meter. Raised on a background thread.</summary>
    public event EventHandler<float>? LevelChanged;

    public bool IsRecording { get; private set; }

    /// <summary>Names of the WinMM input devices (index = device number).</summary>
    public static IReadOnlyList<string> GetInputDeviceNames()
    {
        var names = new List<string>();
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            names.Add(WaveInEvent.GetCapabilities(i).ProductName);
        return names;
    }

    /// <summary>Records a WinMM input device (mono 16-bit PCM) to <paramref name="outputPath"/>.</summary>
    public void StartFromDevice(int deviceNumber, string outputPath, int sampleRate = 44100)
    {
        lock (_gate)
        {
            EnsureNotRecording();
            var format = new WaveFormat(sampleRate, 16, 1);
            _writer = new WaveFileWriter(outputPath, format);
            _path = outputPath;
            _stopped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = format,
                BufferMilliseconds = 50,
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            try
            {
                _waveIn.StartRecording();
            }
            catch
            {
                CleanUp();
                throw;
            }
            IsRecording = true;
        }
    }

    /// <summary>Prepares recording of samples pushed with <see cref="WriteTap"/> (32-bit float WAV).</summary>
    public void StartFromTap(string outputPath, int sampleRate, int channels)
    {
        lock (_gate)
        {
            EnsureNotRecording();
            _writer = new WaveFileWriter(outputPath, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));
            _path = outputPath;
            _stopped = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsRecording = true;
        }
    }

    /// <summary>
    /// Called from the audio callback with the DRY input buffer (before effects).
    /// Ignored when not recording, so the effect chain can always call it.
    /// </summary>
    public void WriteTap(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            if (!IsRecording || _waveIn is not null || _writer is null) return;
            _writer.WriteSamples(buffer, offset, count);
        }
        RaiseLevel(Peak(buffer, offset, count));
    }

    /// <summary>Stops recording and returns the WAV path once the file is closed.</summary>
    public Task<string> StopAsync()
    {
        TaskCompletionSource<string> stopped;
        lock (_gate)
        {
            if (!IsRecording || _stopped is null) throw new InvalidOperationException("Not recording.");
            stopped = _stopped;
            IsRecording = false;

            if (_waveIn is not null)
            {
                _waveIn.StopRecording(); // completes in OnRecordingStopped
                return stopped.Task;
            }

            // Tap mode: close the file immediately.
            var path = _path!;
            CleanUp();
            stopped.TrySetResult(path);
        }
        return stopped.Task;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }

        float peak = 0;
        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            float v = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
            if (v > peak) peak = v;
        }
        RaiseLevel(peak);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        TaskCompletionSource<string>? stopped;
        string? path;
        lock (_gate)
        {
            stopped = _stopped;
            path = _path;
            IsRecording = false;
            CleanUp();
        }

        if (e.Exception is not null) stopped?.TrySetException(e.Exception);
        else stopped?.TrySetResult(path!);
    }

    private void RaiseLevel(float peak) => LevelChanged?.Invoke(this, Math.Min(1f, peak));

    private static float Peak(float[] buffer, int offset, int count)
    {
        float peak = 0;
        for (int i = offset; i < offset + count; i++)
        {
            float v = Math.Abs(buffer[i]);
            if (v > peak) peak = v;
        }
        return peak;
    }

    private void EnsureNotRecording()
    {
        if (IsRecording) throw new InvalidOperationException("Already recording.");
    }

    // Must be called inside _gate.
    private void CleanUp()
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }
        _writer?.Dispose();
        _writer = null;
        _stopped = null;
        _path = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            IsRecording = false;
            _stopped?.TrySetCanceled();
            CleanUp();
        }
    }
}
