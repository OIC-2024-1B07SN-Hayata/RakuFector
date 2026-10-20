using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using ChordDetection.App.Services;
using ChordDetection.Core;
using ChordDetection.Core.Chords;
using ChordDetection.Core.Models;
using ChordDetection.Infrastructure.Audio;
using ChordDetection.Infrastructure.Chroma;
using ChordDetection.Infrastructure.Separation;
using CommonAudio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChordDetection.App.ViewModels;

public sealed record StemOption(string Stem, string Label);

/// <summary>A second mark on the timeline ruler.</summary>
public sealed record TimelineTick(double Left, string Label);

/// <summary>
/// Stage 8: record or open an audio file (WAV / MP3 …) → analyse in the background → show the chords.
/// Changing the display level only re-runs <see cref="ChordSimplifier"/> (no re-analysis).
/// </summary>
public sealed partial class ChordDetectionViewModel : ObservableObject, IDisposable
{
    private readonly ChordDetectionService _service;
    private readonly ChordDetectionService _toneShapedService;
    private readonly ChromaChordDetector _chroma;
    private readonly AudioPreprocessor _preprocessor;
    private readonly ChordRecorder _recorder;
    private readonly ChordSimplifier _simplifier;
    private readonly IFileDialogService _dialogs;
    private readonly ISourceSeparator? _separator;
    private readonly AudioPlayer _player;
    private readonly CommonAudioInput _asio;
    private readonly Func<bool> _sharedAudioConnected;
    private readonly Func<int> _sharedSampleRate;
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    // Length of the pieces fed to the streaming analysis when no separation is used.
    private const double PieceSeconds = 1.0;

    // The empty timeline still shows this many seconds of ruler.
    private const double MinTimelineSeconds = 30;
    private readonly Stopwatch _recordingClock = new();

    private ChordAnalysis? _analysis;
    private CancellationTokenSource? _analysisCts;

    // ASIO recording: the shared input's buffer is copied here (with the input gain) before it is written.
    private volatile bool _recordingWithAsio;
    private float[] _asioBuffer = [];
    private volatile float _asioGain = 1f;

    public ChordDetectionViewModel(
        ChordDetectionService service,
        ChordDetectionService toneShapedService,
        ChromaChordDetector chroma,
        AudioPreprocessor preprocessor,
        ChordRecorder recorder,
        ChordSimplifier simplifier,
        IFileDialogService dialogs,
        ISourceSeparator? separator,
        AudioPlayer player,
        CommonAudioInput asio,
        Func<bool> sharedAudioConnected,
        Func<int> sharedSampleRate)
    {
        _service = service;
        _toneShapedService = toneShapedService;
        _chroma = chroma;
        _preprocessor = preprocessor;
        _recorder = recorder;
        _simplifier = simplifier;
        _dialogs = dialogs;
        _separator = separator;
        _player = player;
        _asio = asio;
        _sharedAudioConnected = sharedAudioConnected;
        _sharedSampleRate = sharedSampleRate;

        _recorder.LevelChanged += OnRecorderLevel;
        _asio.AudioReceived += OnAsioAudio;
        _player.PlaybackEnded += OnPlaybackEnded;
        _playbackTimer.Tick += (_, _) => UpdatePlaybackPosition();

        try
        {
            InputDevices = ChordRecorder.GetInputDeviceNames();
        }
        catch (Exception ex)
        {
            InputDevices = [];
            StatusText = $"入力デバイスを取得できませんでした: {ex.Message}";
        }
        SelectedDeviceIndex = InputDevices.Count > 0 ? 0 : -1;

        try
        {
            AsioDrivers = CommonAudioInput.GetDriverNames();
        }
        catch (Exception ex)
        {
            AsioDrivers = [];
            StatusText = $"ASIO ドライバーを取得できませんでした: {ex.Message}";
        }
        SelectedAsioDriver = AsioDrivers.FirstOrDefault();
        UseAsio = true; // The shell owns the ASIO driver; this page only subscribes to its dry input.
        SelectedSampleRate = SampleRates[0];
        SelectedStem = Stems[0];
        SelectedTuning = Tunings[0];
        RefreshDisplay();
    }

    // ---------- bindable state ----------

    public IReadOnlyList<string> InputDevices { get; }

    /// <summary>
    /// Chords are shown simplified to major / minor / 5 plus <see cref="AlsoShownTypes"/>, without slash bass
    /// (the analysis itself stays detailed). sus2 is still shown as the major chord.
    /// </summary>
    private const DisplayLevel ShownLevel = DisplayLevel.Basic;

    private static readonly IReadOnlySet<string> AlsoShownTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "7", "M7", "m7", "m7b5", "6", "add9", "sus4", "aug", "dim",
    };

    /// <summary>
    /// Reference pitch shown in 設定. Not used by the analysis yet: Basic Pitch's note bins are fixed to A4 = 440 Hz.
    /// </summary>
    public IReadOnlyList<string> Tunings { get; } = ["標準 (440Hz)", "442Hz"];

    [ObservableProperty] private string _selectedTuning = null!;

    /// <summary>Second marks of the ruler, as wide as the timeline.</summary>
    [ObservableProperty] private IReadOnlyList<TimelineTick> _ticks = [];

    /// <summary>Playhead position: the end of the audio analysed so far.</summary>
    [ObservableProperty] private double _playheadLeft;

    /// <summary>Short state under マイク入力: 待機中 / 録音中 m:ss / 解析中….</summary>
    [ObservableProperty] private string _stateLabel = "待機中";

    /// <summary>Colour of the dot in front of <see cref="StateLabel"/>.</summary>
    [ObservableProperty] private string _stateColor = "#8A8D93";

    /// <summary>Text in the middle of the empty timeline; empty once chords are shown.</summary>
    [ObservableProperty] private string _placeholderText = "";

    public string RecordButtonText => IsRecording ? "■  停止して解析" : "●  録音開始";

    /// <summary>Stems of htdemucs_6s that can be analysed after source separation.</summary>
    public IReadOnlyList<StemOption> Stems { get; } =
    [
        new("guitar", "ギター"),
        new("other", "その他（other）"),
    ];

    public ObservableCollection<ChordRowViewModel> Chords { get; } = [];

    /// <summary>False when the Demucs ONNX model is not installed.</summary>
    public bool IsSeparationAvailable => _separator is not null;

    public string SeparationToolTip => IsSeparationAvailable
        ? "バンドの曲などを開くとき、Demucs でギターなどのトラックだけを取り出してから解析します（分離したトラックはドキュメント\\ChordDetectionDemo\\separated に保存）"
        : "Demucs の ONNX モデル（Models\\htdemucs_6s.onnx）がありません。tools/demucs_onnx.py export で作成してください";

    /// <summary>
    /// True for tone-shaped (distorted) sources: weaker notes are accepted, because distortion lowers the
    /// model's probabilities for the played notes. Applies to the next analysis.
    /// </summary>
    [ObservableProperty] private bool _isToneShaped;

    /// <summary>
    /// True to analyse with the chroma baseline (<see cref="ChromaChordDetector"/>) instead of Basic Pitch, to compare the two
    /// on the same audio and labels. <see cref="IsToneShaped"/> does not apply to it. Applies to the next analysis.
    /// </summary>
    [ObservableProperty] private bool _useChroma;

    /// <summary>When true, opened files are separated with Demucs and only <see cref="SelectedStem"/> is analysed.</summary>
    [ObservableProperty] private bool _useSourceSeparation;

    [ObservableProperty] private StemOption _selectedStem = null!;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand), nameof(ToggleRecordingCommand))]
    private int _selectedDeviceIndex;

    // ---------- ASIO input (FX Forge CommonAudio) ----------

    public IReadOnlyList<string> AsioDrivers { get; }

    public bool IsAsioAvailable => AsioDrivers.Count > 0;

    /// <summary>INPUT 1 = 0 (CommonAudioInput.InputChannel). Start fails if the interface has fewer inputs.</summary>
    public IReadOnlyList<string> AsioInputs { get; } = Enumerable.Range(1, 8).Select(i => $"INPUT {i}").ToList();

    public IReadOnlyList<int> SampleRates { get; } = [48000, 44100];

    /// <summary>True to record the guitar through ASIO (CommonAudioInput) instead of the Windows input device.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand), nameof(ToggleRecordingCommand))]
    private bool _useAsio;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand), nameof(ToggleRecordingCommand))]
    private string? _selectedAsioDriver;

    [ObservableProperty] private int _selectedAsioInputIndex;

    [ObservableProperty] private int _selectedSampleRate;

    /// <summary>Gain applied to the ASIO input (dB). A DI guitar is quiet and may fall under the noise gate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InputGainText))]
    private double _inputGainDb;

    public string InputGainText => $"入力ゲイン  +{InputGainDb:0} dB";

    partial void OnInputGainDbChanged(double value) => _asioGain = (float)Math.Pow(10, value / 20);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(RecordButtonText))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand), nameof(StopRecordingCommand), nameof(OpenAudioFileCommand),
        nameof(ToggleRecordingCommand), nameof(ClearCommand))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartRecordingCommand), nameof(OpenAudioFileCommand), nameof(CancelAnalysisCommand),
        nameof(ExportNotesCommand), nameof(ToggleRecordingCommand), nameof(ClearCommand))]
    private bool _isAnalyzing;

    [ObservableProperty] private double _inputLevel;
    [ObservableProperty] private double _analysisProgress;
    [ObservableProperty] private string _statusText = "録音するか、音声ファイル（WAV / MP3 など）を開いてください。";
    [ObservableProperty] private string _sourceName = "（未選択）";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private double _timelineWidth;

    public bool IsIdle => !IsRecording && !IsAnalyzing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayButtonText))]
    private bool _isPlaying;

    /// <summary>Position of the playback line in the timeline (separate from the analysis playhead).</summary>
    [ObservableProperty] private double _playbackLeft;

    [ObservableProperty] private string _playbackTimeText = "";

    /// <summary>True while audio that can be played is loaded (the playback line is shown).</summary>
    [ObservableProperty] private bool _hasPlayback;

    public string PlayButtonText => IsPlaying ? "‖  一時停止" : "▶  再生";

    partial void OnIsRecordingChanged(bool value) => UpdateState();

    partial void OnIsAnalyzingChanged(bool value) => UpdateState();

    private void UpdateState()
    {
        (StateLabel, StateColor) = IsRecording ? ("録音中", "#E53935")
            : IsAnalyzing ? ("解析中…", "#FFB300")
            : ("待機中", "#8A8D93");
        UpdatePlaceholder();
        TogglePlaybackCommand.NotifyCanExecuteChanged();
        StopPlaybackCommand.NotifyCanExecuteChanged();
    }

    private void UpdatePlaceholder() =>
        PlaceholderText = Chords.Count > 0 ? ""
            : IsRecording ? "録音中…弾き終わったら「停止して解析」を押してください"
            : IsAnalyzing ? "解析中…"
            : _analysis is not null ? "コードは検出されませんでした"
            : "「録音開始」を押してギターを弾くか、音声ファイルをここにドロップしてください";

    // ---------- recording ----------

    private bool CanStartRecording() => IsIdle && (UseAsio ? _sharedAudioConnected() && _sharedSampleRate() > 0 : SelectedDeviceIndex >= 0);

    /// <summary>Refresh the record button after shell ASIO connection changes.</summary>
    public void NotifySharedAudioChanged()
    {
        StartRecordingCommand.NotifyCanExecuteChanged();
        ToggleRecordingCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStartRecording))]
    private void StartRecording()
    {
        StopPlayback();
        try
        {
            var path = NewRecordingPath();
            if (UseAsio) StartAsioRecording(path);
            else _recorder.StartFromDevice(SelectedDeviceIndex, path);
            _recordingClock.Restart();
            IsRecording = true;
            StatusText = "録音中…（エフェクトをかける前のドライ音を録音しています）";
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"録音を開始できませんでした。\n{ex.Message}");
        }
    }

    /// <summary>The shared ASIO input pushes its buffers to the recorder's tap (32-bit float mono WAV).</summary>
    private void StartAsioRecording(string path)
    {
        if (!_sharedAudioConnected())
            throw new InvalidOperationException("共通音声入力が未接続です。画面右上の⚙で接続してください。");
        var rate = _sharedSampleRate();
        if (rate <= 0) throw new InvalidOperationException("共通音声入力のサンプルレートが不明です。");
        _recorder.StartFromTap(path, rate, _asio.Channels);
        _recordingWithAsio = true;
    }

    // Called on the ASIO driver thread. The shared buffer must not be modified, so it is copied first.
    private void OnAsioAudio(float[] samples, int sampleRate, int channels)
    {
        if (!_recordingWithAsio) return;
        if (_asioBuffer.Length < samples.Length) _asioBuffer = new float[samples.Length];
        float gain = _asioGain;
        for (int i = 0; i < samples.Length; i++) _asioBuffer[i] = samples[i] * gain;
        _recorder.WriteTap(_asioBuffer, 0, samples.Length);
    }

    private bool CanStopRecording() => IsRecording;

    private bool CanToggleRecording() => IsRecording || CanStartRecording();

    /// <summary>The single record button: starts recording, or stops and analyses.</summary>
    [RelayCommand(CanExecute = nameof(CanToggleRecording))]
    private async Task ToggleRecordingAsync()
    {
        if (IsRecording) await StopRecordingAsync();
        else StartRecording();
    }

    /// <summary>Clears the result (and the reference labels) so the next take starts from an empty timeline.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Clear()
    {
        UnloadPlayback();
        _analysis = null;
        SourceName = "（未選択）";
        SummaryText = "";
        StatusText = "録音するか、音声ファイル（WAV / MP3 など）を開いてください。";
        RefreshDisplay();
    }

    [RelayCommand(CanExecute = nameof(CanStopRecording))]
    private async Task StopRecordingAsync()
    {
        try
        {
            // Stop only this tap; Tone and Tuner must keep receiving the shared ASIO stream.
            _recordingWithAsio = false;
            var asioError = _asio.LastError;
            var path = await _recorder.StopAsync();
            _recordingClock.Stop();
            if (asioError is not null) _dialogs.ShowError($"ASIO 入力でエラーがありました。\n{asioError}");
            IsRecording = false;
            InputLevel = 0;
            await AnalyzeFileAsync(path);
        }
        catch (Exception ex)
        {
            IsRecording = false;
            _dialogs.ShowError($"録音の停止に失敗しました。\n{ex.Message}");
        }
    }

    private void OnRecorderLevel(object? sender, float peak)
    {
        // Raised on the audio thread; WPF marshals scalar property notifications.
        InputLevel = peak;
        if (IsRecording)
        {
            StatusText = $"録音中… {_recordingClock.Elapsed:m\\:ss}（ドライ音）";
            StateLabel = $"録音中 {_recordingClock.Elapsed:m\\:ss}";
        }
    }

    /// <summary>Documents\ChordDetectionDemo\separated\&lt;file&gt;_&lt;stem&gt;_&lt;time&gt;.wav (kept so it can be listened to).</summary>
    private static string SeparationOutputPath(string sourcePath, string stem)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChordDetectionDemo", "separated");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(sourcePath)}_{stem}_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
    }

    private static string NewRecordingPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ChordDetectionDemo");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
    }

    // ---------- analysis ----------

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task OpenAudioFileAsync()
    {
        var path = _dialogs.OpenFile("音声ファイルを開く", AudioFileLoader.DialogFilter);
        if (path is not null) await AnalyzeOpenedFileAsync(path);
    }

    /// <summary>True when a file with this extension can be opened (used for drag and drop).</summary>
    public static bool IsSupportedAudioFile(string path) =>
        AudioFileLoader.SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Analyses a file dropped on the window, with the same options as 音声ファイルを開く.</summary>
    public async Task OpenDroppedFileAsync(string path)
    {
        if (!IsIdle) return;
        if (!IsSupportedAudioFile(path))
        {
            _dialogs.ShowError($"この形式は開けません（{Path.GetExtension(path)}）。\n対応形式: {string.Join(" ", AudioFileLoader.SupportedExtensions)}");
            return;
        }
        await AnalyzeOpenedFileAsync(path);
    }

    private Task AnalyzeOpenedFileAsync(string path) =>
        AnalyzeFileAsync(path, UseSourceSeparation && IsSeparationAvailable ? SelectedStem.Stem : null);

    /// <param name="stem">Stem to analyse after Demucs separation; null analyses the file as it is.</param>
    private async Task AnalyzeFileAsync(string path, string? stem = null)
    {
        UnloadPlayback();
        _analysisCts = new CancellationTokenSource();
        var ct = _analysisCts.Token;
        IsAnalyzing = true;
        AnalysisProgress = 0;
        SourceName = stem is null ? Path.GetFileName(path) : $"{Path.GetFileName(path)}（{SelectedStem.Label}）";
        _analysis = null;
        RefreshDisplay();
        var clock = Stopwatch.StartNew();
        bool finished = false;

        try
        {
            StatusText = "読み込み中…";
            var audio = await Task.Run(() => AudioFileLoader.Load(path), ct); // stage 1 output (WAV / MP3 …)
            LoadPlayback(audio); // always the original sound, also when a separated stem is analysed
            double total = audio.DurationSeconds;
            string? stemPath = stem is null ? null : SeparationOutputPath(path, stem);
            bool chroma = UseChroma;
            StatusText = stem is not null
                ? "音源分離中…（Demucs。最初の結果は約8秒分の分離が終わると表示されます）"
                : chroma ? "解析中…（クロマ方式）" : "解析中…（Basic Pitch で音を検出しています）";

            // Intermediate results: shown as they come; the last part may still change.
            var updates = new Progress<ChordAnalysis>(partial =>
            {
                if (finished) return;
                _analysis = partial;
                RefreshDisplay();                                           // stage 7
                AnalysisProgress = total > 0 ? Math.Min(1, partial.DurationSeconds / total) : 0;
                StatusText = $"{(stem is null ? "解析中" : "音源分離・解析中")}… {partial.DurationSeconds:0} / {total:0} 秒（途中結果。最後の部分は変わることがあります）";
            });

            var analysis = await Task.Run(() =>
            {
                // Pieces flow through every stage: (separation →) preprocessing → model → chords.
                var pieces = stem is null
                    ? audio.Split(PieceSeconds)
                    : WavFileSaver.Tee(_separator!.Separate(audio, stem, ct), stemPath!);
                var mono = _preprocessor.ProcessStream(pieces);             // stage 2
                if (chroma) return _chroma.AnalyzeStreaming(mono, updates, ct);
                var service = IsToneShaped ? _toneShapedService : _service;
                return service.AnalyzeStreaming(mono, updates, null, ct);   // stages 3–6
            }, ct);

            finished = true;
            _analysis = analysis;
            RefreshDisplay();                                               // stage 7
            StatusText = $"解析完了（{(chroma ? "クロマ方式, " : "")}{clock.Elapsed.TotalSeconds:0.0} 秒） {stemPath ?? path}";
        }
        catch (OperationCanceledException)
        {
            finished = true;
            StatusText = "解析をキャンセルしました。";
        }
        catch (Exception ex)
        {
            finished = true;
            UnloadPlayback();
            StatusText = "解析に失敗しました。";
            _dialogs.ShowError($"解析に失敗しました。\n{ex.Message}");
        }
        finally
        {
            IsAnalyzing = false;
            AnalysisProgress = 0;
            _analysisCts.Dispose();
            _analysisCts = null;
        }
    }

    private bool CanCancelAnalysis() => IsAnalyzing;

    [RelayCommand(CanExecute = nameof(CanCancelAnalysis))]
    private void CancelAnalysis() => _analysisCts?.Cancel();

    // ---------- playback ----------

    private bool CanPlay() => IsIdle && HasPlayback;

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void TogglePlayback()
    {
        if (IsPlaying)
        {
            _player.Pause();
            SetPlaying(false);
            return;
        }
        try
        {
            _player.Play();
            SetPlaying(true);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError($"再生できませんでした。\n{ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void StopPlayback()
    {
        _player.Stop();
        SetPlaying(false);
    }

    /// <summary>Plays from <paramref name="seconds"/> (clicking a chord in the timeline).</summary>
    public void PlayFrom(double seconds)
    {
        if (!CanPlay()) return;
        _player.Seek(seconds);
        if (!IsPlaying) TogglePlayback();
        else UpdatePlaybackPosition();
    }

    private void LoadPlayback(AudioData audio)
    {
        _player.Load(audio);
        HasPlayback = true;
        SetPlaying(false);
    }

    private void UnloadPlayback()
    {
        _player.Unload();
        HasPlayback = false;
        SetPlaying(false);
    }

    private void OnPlaybackEnded(object? sender, EventArgs e) => SetPlaying(false);

    private void SetPlaying(bool playing)
    {
        IsPlaying = playing;
        if (playing) _playbackTimer.Start();
        else _playbackTimer.Stop();
        UpdatePlaybackPosition();
        TogglePlaybackCommand.NotifyCanExecuteChanged();
        StopPlaybackCommand.NotifyCanExecuteChanged();
    }

    private void UpdatePlaybackPosition()
    {
        double position = _player.PositionSeconds;
        PlaybackLeft = position * ChordRowViewModel.PixelsPerSecond;
        PlaybackTimeText = HasPlayback ? $"{FormatTime(position)} / {FormatTime(_player.DurationSeconds)}" : "";
    }

    private static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss", CultureInfo.InvariantCulture);

    // ---------- display (stage 7) ----------

    private void RefreshDisplay()
    {
        Chords.Clear();
        double seconds = _analysis?.DurationSeconds ?? 0;
        PlayheadLeft = seconds * ChordRowViewModel.PixelsPerSecond;
        int tickCount = (int)Math.Ceiling(Math.Max(MinTimelineSeconds, seconds + 2));
        TimelineWidth = tickCount * ChordRowViewModel.PixelsPerSecond;
        Ticks = Enumerable.Range(0, tickCount)
            .Select(s => new TimelineTick(s * ChordRowViewModel.PixelsPerSecond, $"{s / 60:00}:{s % 60:00}"))
            .ToList();

        if (_analysis is null)
        {
            UpdatePlaceholder();
            return;
        }

        // N.C. (no chord) is not shown; in the timeline its time is left empty.
        foreach (var chord in _simplifier.SimplifyTimeline(_analysis.Chords, ShownLevel, alsoShown: AlsoShownTypes).Where(c => !c.Chord.IsNoChord))
            Chords.Add(new ChordRowViewModel(chord, NotesIn(chord)));

        UpdatePlaceholder();
        SummaryText = $"{_analysis.DurationSeconds:0.0} 秒 / 検出した音 {_analysis.Notes.Count} 個 / コード {Chords.Count} 個";
    }

    private string NotesIn(DisplayedChord shown) =>
        string.Join(" ", _analysis!.Notes
            .Where(n => n.StartSeconds < shown.EndSeconds && n.EndSeconds > shown.StartSeconds)
            .Select(n => n.Pitch)
            .Distinct()
            .Order()
            .Select(PitchClasses.MidiName));

    // ---------- export ----------

    private bool HasAnalysis() => _analysis is not null && !IsAnalyzing;

    /// <summary>Exports the stage-4 note list for comparison with the official Python version (tools/compare_notes.py).</summary>
    [RelayCommand(CanExecute = nameof(HasAnalysis))]
    private void ExportNotes()
    {
        var path = _dialogs.SaveFile("音のリストを保存", "CSV (*.csv)|*.csv",
            Path.ChangeExtension(SourceName, null) + "_notes.csv");
        if (path is null) return;

        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string> { "start,end,pitch,amplitude" };
        lines.AddRange(_analysis!.Notes.Select(n => string.Join(",",
            n.StartSeconds.ToString("R", inv), n.EndSeconds.ToString("R", inv),
            n.Pitch.ToString(inv), n.Amplitude.ToString("R", inv))));
        File.WriteAllLines(path, lines);
        StatusText = $"音のリストを保存しました: {path}";
    }

    public void Dispose()
    {
        _recorder.LevelChanged -= OnRecorderLevel;
        _asio.AudioReceived -= OnAsioAudio;
        // MainWindow owns the shared input and disposes it once on application close.
        _player.PlaybackEnded -= OnPlaybackEnded;
        _playbackTimer.Stop();
        _analysisCts?.Cancel();
        _recorder.Dispose();
        _player.Dispose();
    }
}
