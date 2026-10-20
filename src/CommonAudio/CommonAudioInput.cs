using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CommonAudio;

/// <summary>ASIOの1入力をfloat PCMとして受信先へ渡し、任意で音作り処理後の音声を左右へ出力します。</summary>
public sealed class CommonAudioInput : IDisposable
{
    private AsioOut? _asio;
    private AsioMonitorWaveProvider? _monitor;
    private PcMonitorWaveProvider? _pcMonitor;
    private WasapiOut? _pcPlayer;
    private MMDevice? _pcDevice;
    private float[] _samples = Array.Empty<float>();
    private float[] _outputSamples = Array.Empty<float>();
    private float _inputPeak, _outputPeak;
    private long _receivedBlocks;
    private int _testFramesRemaining;
    private double _testPhase;
    private int _sampleRate;
    private int _stopping = 1;
    private int _callbacks;
    private string? _lastError;

    // 選択した入力1本をモノラルとして渡します。
    public int Channels => 1;

    // 0 = INPUT 1、1 = INPUT 2。Channelsとは別の値です。
    public int InputChannel { get; private set; }
    public string? LastError => Volatile.Read(ref _lastError) ?? _monitor?.LastError;
    public float InputPeak => Volatile.Read(ref _inputPeak);
    public float OutputPeak => Volatile.Read(ref _outputPeak);
    public long ReceivedBlocks => Interlocked.Read(ref _receivedBlocks);
    public long OutputBlocks => _pcMonitor?.ReadCount ?? _monitor?.ReadCount ?? 0;
    public string? PcOutputName { get; private set; }
    public int OutputChannel { get; private set; }
    public int AsioBufferFrames { get; private set; }
    public int PcBufferMilliseconds { get; private set; }
    public double PcQueuedMilliseconds => _sampleRate > 0 ? (_pcMonitor?.BufferedFrames ?? 0) * 1000.0 / _sampleRate : 0;
    public bool IsOutputTestActive => Volatile.Read(ref _testFramesRemaining) > 0;

    /// <summary>
    /// 引数はsamples、sampleRate、channelsです。
    /// samplesは再利用されます。コールバック中だけ読み取り、変更しないでください。
    /// 後から使用する場合は受信側のバッファへコピーしてください。
    /// </summary>
    public event Action<float[], int, int>? AudioReceived;

    // 出力専用のコピーを加工します。音声スレッドで呼ばれるのでUI操作は禁止。
    public Action<float[], int>? ProcessOutput { get; set; }
    // 入力停止後、再接続の開始前にフィルターなどの状態をリセットします。
    public Action? ResetOutput { get; set; }

    public static string[] GetDriverNames() => AsioOut.GetDriverNames();

    /// <summary>UIスレッドなどの制御側から呼び出します。</summary>
    public void Start(string driverName, int inputChannel = 0, int sampleRate = 48000,
        bool monitorOutput = true, int outputChannel = 0, bool pcOutput = false,
        string? pcDeviceId = null, int pcBufferMilliseconds = AudioLatency.DefaultPcMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(driverName))
            throw new ArgumentException("ASIOドライバー名を指定してください。", nameof(driverName));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (pcBufferMilliseconds < 5 || pcBufferMilliseconds > 100)
            throw new ArgumentOutOfRangeException(nameof(pcBufferMilliseconds));

        Stop();
        var asio = new AsioOut(driverName);
        _asio = asio;

        try
        {
            if (inputChannel < 0 || inputChannel >= asio.DriverInputChannelCount)
                throw new ArgumentOutOfRangeException(nameof(inputChannel));
            if (!asio.IsSampleRateSupported(sampleRate))
                throw new ArgumentException("このサンプリング周波数は使用できません。", nameof(sampleRate));
            bool asioOutput = monitorOutput && !pcOutput;
            if (asioOutput && (outputChannel < 0 || outputChannel + 2 > asio.DriverOutputChannelCount))
                throw new ArgumentException("指定した出力番号から連続した2チャンネルが必要です。");

            _sampleRate = sampleRate;
            InputChannel = inputChannel;
            Volatile.Write(ref _lastError, null);
            asio.InputChannelOffset = inputChannel;
            asio.ChannelOffset = asioOutput ? outputChannel : 0;
            OutputChannel = outputChannel;
            Interlocked.Exchange(ref _receivedBlocks, 0);
            Volatile.Write(ref _testFramesRemaining, 0);
            Volatile.Write(ref _inputPeak, 0); Volatile.Write(ref _outputPeak, 0);
            _testPhase = 0;
            ResetOutput?.Invoke();

            // ASIO出力の場合のみ同じドライバーで再生。PC出力時のASIOは入力専用。
            _monitor = asioOutput ? new AsioMonitorWaveProvider(sampleRate) : null;
            asio.InitRecordAndPlayback(_monitor, Channels, sampleRate);
            AsioBufferFrames = asio.FramesPerBuffer;
            PcBufferMilliseconds = pcBufferMilliseconds;
            _samples = new float[AsioBufferFrames * Channels];
            _outputSamples = new float[_samples.Length];
            _monitor?.Prepare(AsioBufferFrames);
            if (monitorOutput && pcOutput)
            {
                using var enumerator = new MMDeviceEnumerator();
                _pcDevice = string.IsNullOrWhiteSpace(pcDeviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : enumerator.GetDevice(pcDeviceId);
                PcOutputName = _pcDevice.FriendlyName;
                _pcMonitor = new PcMonitorWaveProvider(sampleRate,
                    AudioLatency.QueueCapacityFrames(sampleRate, AsioBufferFrames, pcBufferMilliseconds));
                // 共有モードで、Windows側の周波数・フォーマット変換を許可。
                _pcPlayer = new WasapiOut(_pcDevice, AudioClientShareMode.Shared, true, pcBufferMilliseconds);
                _pcPlayer.PlaybackStopped += OnPcPlaybackStopped;
                _pcPlayer.Init(_pcMonitor);
            }
            asio.AudioAvailable += OnAudioAvailable;
            Volatile.Write(ref _stopping, 0);
            asio.Play();
            _pcPlayer?.Play();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        Interlocked.Increment(ref _callbacks);
        try
        {
            if (Volatile.Read(ref _stopping) != 0 || !ReferenceEquals(sender, _asio))
                return;

            if (e.SamplesPerBuffer * Channels != _samples.Length)
            {
                ClearOutput();
                Volatile.Write(ref _lastError, "バッファサイズが変わりました。再接続してください。");
                return;
            }

            e.GetAsInterleavedSamples(_samples);
            Interlocked.Increment(ref _receivedBlocks);
            Volatile.Write(ref _inputPeak, Peak(_samples));
            AudioReceived?.Invoke(_samples, _sampleRate, Channels);
            if (_monitor is not null || _pcMonitor is not null)
            {
                Array.Copy(_samples, _outputSamples, _samples.Length);
                int remaining = Volatile.Read(ref _testFramesRemaining);
                if (remaining > 0)
                {
                    // 出力経路の確認用。エフェクトは通さず、低音量で約3秒だけ鳴らす。
                    double step = 2 * Math.PI * 440 / _sampleRate;
                    int frames = Math.Min(remaining, _outputSamples.Length);
                    for (int i = 0; i < _outputSamples.Length; i++)
                    {
                        _outputSamples[i] = i < frames ? (float)(.025 * Math.Sin(_testPhase)) : 0;
                        _testPhase += step;
                        if (_testPhase >= 2 * Math.PI) _testPhase -= 2 * Math.PI;
                    }
                    Interlocked.Add(ref _testFramesRemaining, -frames);
                }
                else ProcessOutput?.Invoke(_outputSamples, _sampleRate);
                for (int i = 0; i < _outputSamples.Length; i++)
                    _outputSamples[i] = float.IsFinite(_outputSamples[i]) ? Math.Clamp(_outputSamples[i], -1, 1) : 0;
                Volatile.Write(ref _outputPeak, Peak(_outputSamples));
                _monitor?.SetInput(_outputSamples);
                _pcMonitor?.SetInput(_outputSamples);
            }
        }
        catch (Exception ex)
        {
            // ASIOのコールバック外へ受信側の例外を投げないようにします。
            ClearOutput();
            Volatile.Write(ref _lastError, ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _callbacks);
        }
    }

    public void StartOutputTest(int seconds = 3)
    {
        if ((_monitor is null && _pcMonitor is null) || Volatile.Read(ref _stopping) != 0)
            throw new InvalidOperationException("⚙からギター出力をONにして接続してください。");
        Volatile.Write(ref _testFramesRemaining, _sampleRate * Math.Clamp(seconds, 1, 5));
    }

    private void ClearOutput()
    {
        _monitor?.Clear();
        _pcMonitor?.Clear();
    }

    private void OnPcPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (Volatile.Read(ref _stopping) == 0 && ReferenceEquals(sender, _pcPlayer))
        {
            ClearOutput();
            Volatile.Write(ref _lastError, "PC出力が停止しました。⚙から再接続してください。" +
                (e.Exception is null ? "" : " " + e.Exception.Message));
        }
    }

    private static float Peak(float[] samples)
    {
        float peak = 0;
        for (int i = 0; i < samples.Length; i++)
            if (float.IsFinite(samples[i])) peak = Math.Max(peak, Math.Abs(samples[i]));
        return peak;
    }

    /// <summary>AudioReceivedの中から呼ばず、制御側から呼び出します。</summary>
    public void Stop()
    {
        Volatile.Write(ref _stopping, 1);
        var asio = _asio;
        if (asio is not null)
        {
            asio.AudioAvailable -= OnAudioAvailable;
            asio.Stop();
        }

        bool completed = SpinWait.SpinUntil(
            () => Volatile.Read(ref _callbacks) == 0,
            TimeSpan.FromSeconds(2));
        if (!completed)
            throw new TimeoutException("音声処理の停止待ちがタイムアウトしました。");

        // ASIOコールバック終了後にPC再生を停止し、共有バッファを解放します。
        if (_pcPlayer is not null)
        {
            _pcPlayer.PlaybackStopped -= OnPcPlaybackStopped;
            _pcPlayer.Stop();
            _pcPlayer.Dispose();
            _pcPlayer = null;
        }
        _pcDevice?.Dispose();
        _pcDevice = null;
        _pcMonitor?.Clear();
        _pcMonitor = null;
        PcOutputName = null;
        AsioBufferFrames = 0;
        asio?.Dispose();
        _asio = null;
        _monitor = null;
        Volatile.Write(ref _testFramesRemaining, 0);
        Volatile.Write(ref _inputPeak, 0); Volatile.Write(ref _outputPeak, 0);
    }

    public void Dispose() => Stop();
}
