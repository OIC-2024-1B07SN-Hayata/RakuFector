using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using ChordDetection.App.Services;
using ChordDetection.Core;
using ChordDetection.Core.Chords;
using ChordDetection.Core.Keys;
using ChordDetection.Core.Models;
using ChordDetection.Core.Music;
using ChordDetection.Infrastructure.Audio;
using ChordDetection.Infrastructure.Chroma;
using ChordDetection.Infrastructure.Separation;
using CommonAudio;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geometry = System.Windows.Media.Geometry;
using Point = System.Windows.Point;
using StreamGeometry = System.Windows.Media.StreamGeometry;
using StreamGeometryContext = System.Windows.Media.StreamGeometryContext;

namespace ChordDetection.App.ViewModels;

public sealed record StemOption(string Stem, string Label);

/// <summary>A bar or beat number on the timeline ruler, as in the drum editor ("3" for a bar, "3.2" for its 2nd beat).</summary>
public sealed record BeatMark(double Left, string Label, bool IsBar);

/// <summary>Fineness of the thin lines between the beats in the timeline.</summary>
/// <param name="Division">Lines per whole note: 4 = beats only, 16 = sixteenth notes.</param>
public sealed record LineDivisionOption(int Division, string Label);

/// <summary>One capo position in the capo selector.</summary>
public sealed record CapoOption(int Value, string Label);

/// <summary>One choice of the timeline width: about <paramref name="Seconds"/> fit without scrolling.</summary>
public sealed record TimelineZoomOption(double Seconds, string Label);

/// <summary>One choice of the minor-key degree base selector.</summary>
public sealed record MinorDegreeBaseOption(MinorDegreeBase Base, string Label);

/// <summary>One row of the capo suggestions.</summary>
/// <param name="Capo">Capo fret applied when the row is clicked.</param>
/// <param name="Shapes">Shapes to fret there, e.g. "G  C  D  Em".</param>
/// <param name="IsCurrent">True when this capo is the one selected now.</param>
public sealed record CapoSuggestionRow(int Capo, string Title, string Shapes, string ScoreText, bool IsCurrent);

/// <summary>One choice of the scale shown on the fretboard.</summary>
public sealed record ScaleOption(ScaleType Type, string Label);

/// <summary>What is written in the fretboard dots.</summary>
public enum FretLabelMode { NoteName, Interval }

/// <summary>One choice of the fretboard label selector.</summary>
public sealed record FretLabelOption(FretLabelMode Mode, string Label);

/// <summary>One dot on the fretboard. <paramref name="StringIndex"/> 0 is the 1st (highest) string.</summary>
public sealed record FretMarkViewModel(int StringIndex, int Fret, string Label, FretMarkKind Kind);

/// <summary>Tab of the fretboard panel (指板).</summary>
public enum FretboardTab { ChordForm, Scale }

/// <summary>A detected note under the current chord; <paramref name="IsChordTone"/> false is drawn dashed.</summary>
public sealed record DetectedNoteChip(string Name, bool IsChordTone);

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
    private readonly KeyEstimator _keyEstimator = new();
    private KeyResult? _key;
    private readonly CapoAdvisor _capoAdvisor = new();
    private readonly ChordHistory _history = new(); // chords of finished analyses, for the capo suggestions
    private bool _keyDecided; // true once the whole performance is analysed (also when no key was found)
    private readonly IFileDialogService _dialogs;
    private readonly ISourceSeparator? _separator;
    private readonly AudioPlayer _player;
    private readonly CommonAudioInput _asio;
    private readonly Func<bool> _sharedAudioConnected;
    private readonly Func<int> _sharedSampleRate;
    private readonly DispatcherTimer _playbackTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };

    // Bar-synced recording: the app's tempo (drum editor) and the bars of the current take.
    private readonly ITempoSource? _tempo;
    private readonly BarChordQuantizer _barQuantizer = new();
    private BarGrid? _barGrid;     // null: no bars (files, or recorded without 小節に合わせる)
    private bool _tempoStarted;    // true while this page runs the metronome / drums for a recording

    // Length of the pieces fed to the streaming analysis when no separation is used.
    private const double PieceSeconds = 1.0;

    // The empty timeline still shows at least this many seconds of ruler.
    private const double MinTimelineSeconds = 30;

    // Width of the timeline in a 1600 px window; the zoom choices are seconds per this width.
    private const double TimelineViewWidth = 1020;

    // Candidate seconds between ruler labels; the first one leaving MinRulerLabelSpacing is used.
    private static readonly int[] RulerLabelSteps = [1, 2, 5, 10, 15, 30, 60];
    private const double MinRulerLabelSpacing = 64;
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
        Func<int> sharedSampleRate,
        ITempoSource? tempo = null)
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
        _tempo = tempo;
        _selectedLineDivision = LineDivisions[^1];
        _selectedMinorDegreeBase = MinorDegreeBases[0];
        _selectedTimelineZoom = TimelineZooms[2]; // about 30 s
        _selectedScale = ScaleOptions[0];
        _selectedFretLabel = FretLabels[0];
        if (_tempo is not null) _tempo.TempoChanged += OnTempoChanged;

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
        ApplySettings(ChordPageSettings.Load());
        RefreshDisplay();

        // Keep the settings for the next run: saved shortly after the last change (a dragged slider saves once).
        _settingsSaveTimer.Tick += (_, _) => { _settingsSaveTimer.Stop(); CurrentSettings().Save(); };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name && SavedSettings.Contains(name)) { _settingsSaveTimer.Stop(); _settingsSaveTimer.Start(); }
        };
    }

    // ---------- settings kept between runs ----------

    private readonly DispatcherTimer _settingsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    private static readonly IReadOnlySet<string> SavedSettings = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(SelectedTimelineZoom), nameof(ShowDegrees), nameof(SelectedMinorDegreeBase), nameof(SelectedLineDivision),
        nameof(SelectedFretLabel), nameof(IsFretboardOpen), nameof(SelectedFretboardTab), nameof(FretboardHeight),
    };

    /// <summary>Restores saved settings; values that are no longer offered are left at their defaults.</summary>
    private void ApplySettings(ChordPageSettings? saved)
    {
        if (saved is null) return;
        SelectedTimelineZoom = TimelineZooms.FirstOrDefault(z => z.Seconds == saved.TimelineSeconds) ?? SelectedTimelineZoom;
        ShowDegrees = saved.WantsDegrees;
        SelectedMinorDegreeBase = MinorDegreeBases.FirstOrDefault(b => b.Base == saved.MinorBase) ?? SelectedMinorDegreeBase;
        SelectedLineDivision = LineDivisions.FirstOrDefault(d => d.Division == saved.LineDivision) ?? SelectedLineDivision;
        if (Enum.TryParse<FretLabelMode>(saved.FretLabelMode, out var fretLabel))
            SelectedFretLabel = FretLabels.FirstOrDefault(l => l.Mode == fretLabel) ?? SelectedFretLabel;
        IsFretboardOpen = saved.FretboardOpen;
        if (saved.FretboardHeight > 0) FretboardHeight = saved.FretboardHeight;
        if (Enum.TryParse<FretboardTab>(saved.FretboardTab, out var tab)) SelectedFretboardTab = tab;
    }

    private ChordPageSettings CurrentSettings() => new()
    {
        TimelineSeconds = SelectedTimelineZoom?.Seconds ?? 30,
        ShowDegrees = ShowDegrees,
        MinorDegreeBase = (SelectedMinorDegreeBase?.Base ?? MinorDegreeBase.RelativeMajor).ToString(),
        LineDivision = SelectedLineDivision?.Division ?? 32,
        FretLabelMode = (SelectedFretLabel?.Mode ?? FretLabelMode.NoteName).ToString(),
        FretboardOpen = IsFretboardOpen,
        FretboardTab = SelectedFretboardTab.ToString(),
        FretboardHeight = FretboardHeight,
    };

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

    /// <summary>True once the analysis has finished; the key badge is hidden for intermediate results.</summary>
    [ObservableProperty] private bool _isKeyVisible;

    /// <summary>Key in the badge, e.g. "Am"; "≈ Am" when major / minor was estimated; "-" when undecided.</summary>
    [ObservableProperty] private string _keyName = "";

    /// <summary>Second part of the badge, e.g. "A マイナー ・ 平行調 C".</summary>
    [ObservableProperty] private string _keyDetail = "";

    // ---------- transposition / capo ----------

    /// <summary>Semitones the shown chords and key are moved by (−6 … +6); display only, no re-analysis.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransposeText))]
    private int _transpose;

    /// <summary>Capo fret (0 = none … 7). The timeline then also shows the shape to fret.</summary>
    [ObservableProperty] private int _capo;

    public string TransposeText => Transpose == 0 ? "0" : Transpose.ToString("+0;-0", CultureInfo.InvariantCulture);

    public IReadOnlyList<CapoOption> CapoOptions { get; } =
        [.. Enumerable.Range(Transposer.MinCapo, Transposer.MaxCapo - Transposer.MinCapo + 1)
            .Select(c => new CapoOption(c, c == 0 ? "なし" : c.ToString(CultureInfo.InvariantCulture)))];

    /// <summary>The three easiest capo positions; empty while nothing is known.</summary>
    [ObservableProperty] private IReadOnlyList<CapoSuggestionRow> _capoSuggestions = [];

    /// <summary>What the suggestions are based on, e.g. "弾いたコードから（4 種類）"; "-" when there are none.</summary>
    [ObservableProperty] private string _capoSuggestionSourceText = "-";

    public string RecordButtonText => IsRecording ? "■  停止して解析" : "●  録音開始";

    // ---------- bars (tempo of the drum editor) ----------

    /// <summary>False when no tempo source is connected (小節に合わせる is then unavailable).</summary>
    public bool IsTempoAvailable => _tempo is not null;

    /// <summary>
    /// When true, recording starts the drum editor's pattern (drums / metronome) from bar 1 and plays it until the
    /// recording stops. Applies to the next recording.
    /// </summary>
    [ObservableProperty] private bool _playDrumsWhileRecording;

    /// <summary>
    /// When true, the chords of the take are also shown bar by bar of the drums (needs
    /// <see cref="PlayDrumsWhileRecording"/>, which is turned on with it). Applies to the next recording.
    /// </summary>
    [ObservableProperty] private bool _syncToBars;

    partial void OnPlayDrumsWhileRecordingChanged(bool value)
    {
        if (!value) SyncToBars = false;
    }

    partial void OnSyncToBarsChanged(bool value)
    {
        if (value) PlayDrumsWhileRecording = true;
        RefreshGridLines(); // the lines appear as soon as 小節に合わせる is chosen
    }

    /// <summary>"♩=120  4/4" from the drum editor.</summary>
    public string TempoText => _tempo is null ? "-" : $"♩={_tempo.Bpm}  {_tempo.BeatsPerBar}/4";

    private void OnTempoChanged()
    {
        OnPropertyChanged(nameof(TempoText));
        OnPropertyChanged(nameof(MetronomeEnabled));
        RefreshGridLines(); // the preview lines follow the drum editor's tempo
    }

    /// <summary>Metronome click of the drum editor's pattern (same switch as its メトロノーム button).</summary>
    public bool MetronomeEnabled
    {
        get => _tempo?.MetronomeEnabled == true;
        set
        {
            if (_tempo is null || _tempo.MetronomeEnabled == value) return;
            _tempo.MetronomeEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Level of the drums, 0–100 %. Can be changed while recording.</summary>
    public double DrumVolumePercent
    {
        get => (_tempo?.DrumVolume ?? 1) * 100;
        set
        {
            if (_tempo is null) return;
            _tempo.DrumVolume = Math.Clamp(value, 0, 100) / 100;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DrumVolumeText));
        }
    }

    public string DrumVolumeText => $"{DrumVolumePercent:0}%";

    /// <summary>Level of the metronome click, 0–100 %. Can be changed while recording.</summary>
    public double MetronomeVolumePercent
    {
        get => (_tempo?.MetronomeVolume ?? 1) * 100;
        set
        {
            if (_tempo is null) return;
            _tempo.MetronomeVolume = Math.Clamp(value, 0, 100) / 100;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MetronomeVolumeText));
        }
    }

    public string MetronomeVolumeText => $"{MetronomeVolumePercent:0}%";

    /// <summary>
    /// Length of the bar and beat lines. The timeline's height follows the window, so they are drawn longer than it
    /// can be and cut at its bottom by the view.
    /// </summary>
    public const double TimelineHeight = 2000;

    /// <summary>Top of the thin lines between the beats: they start as ticks at the bottom of the ruler.</summary>
    public const double SubdivisionTop = 40;

    /// <summary>Narrowest space for a ruler number (as in the drum editor).</summary>
    private const double MinLabelSpacing = 48;

    /// <summary>Narrowest space between two beat / grid lines.</summary>
    private const double MinLineSpacing = 5;

    /// <summary>Bar lines in the timeline; null without bars.</summary>
    [ObservableProperty] private Geometry? _barLines;

    /// <summary>Beat lines between the bar lines in the timeline; null without bars.</summary>
    [ObservableProperty] private Geometry? _beatLines;

    /// <summary>Thin lines between the beats (<see cref="SelectedLineDivision"/>); null without bars or with beats only.</summary>
    [ObservableProperty] private Geometry? _subdivisionLines;

    /// <summary>Bar and beat numbers on the ruler; empty without bars.</summary>
    [ObservableProperty] private IReadOnlyList<BeatMark> _beatMarks = [];

    public IReadOnlyList<LineDivisionOption> LineDivisions { get; } =
    [
        new(4, "拍のみ"),
        new(8, "8 分音符"),
        new(16, "16 分音符"),
        new(32, "32 分音符"),
    ];

    /// <summary>Fineness of the thin lines in the timeline; can be changed after recording.</summary>
    [ObservableProperty] private LineDivisionOption? _selectedLineDivision;

    partial void OnSelectedLineDivisionChanged(LineDivisionOption? value) => RefreshGridLines();

    /// <summary>True while the timeline shows bar / beat lines.</summary>
    [ObservableProperty] private bool _hasGridLines;

    /// <summary>Length of the timeline in seconds (the lines are drawn to its end).</summary>
    private double _timelineSeconds = MinTimelineSeconds;

    // ---------- timeline width (seconds per screen) ----------

    public IReadOnlyList<TimelineZoomOption> TimelineZooms { get; } =
    [
        new(10, "約 10 秒"),
        new(20, "約 20 秒"),
        new(30, "約 30 秒"),
        new(60, "約 1 分"),
        new(120, "約 2 分"),
    ];

    /// <summary>How many seconds the timeline shows without scrolling; can be changed at any time.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixelsPerSecond))]
    private TimelineZoomOption? _selectedTimelineZoom;

    partial void OnSelectedTimelineZoomChanged(TimelineZoomOption? value)
    {
        RefreshDisplay();
        UpdatePlaybackPosition();
    }

    /// <summary>Timeline scale from <see cref="SelectedTimelineZoom"/> (30 s → 34 px per second).</summary>
    public double PixelsPerSecond => TimelineViewWidth / (SelectedTimelineZoom?.Seconds ?? 30);

    /// <summary>Seconds between the labels of the ruler, so that the labels do not overlap.</summary>
    private int RulerLabelSeconds =>
        RulerLabelSteps.FirstOrDefault(s => s * PixelsPerSecond >= MinRulerLabelSpacing, RulerLabelSteps[^1]);

    /// <summary>
    /// Bars drawn in the timeline: those of a take recorded with 小節に合わせる; otherwise, while 小節に合わせる is
    /// chosen, the drum editor's tempo with bar 1 at 0 s (a guide before / without a synced recording).
    /// </summary>
    private BarGrid? DisplayGrid =>
        _barGrid ?? (SyncToBars && _tempo is not null ? new BarGrid(_tempo.Bpm, _tempo.BeatsPerBar) : null);

    /// <summary>
    /// Bar lines, beat lines and thin lines between them (as fine as <see cref="SelectedLineDivision"/>) of
    /// <see cref="DisplayGrid"/> to the end of the timeline, with bar / beat numbers on the ruler like the drum editor.
    /// </summary>
    /// <summary>
    /// Lines per beat drawn in the timeline: <see cref="SelectedLineDivision"/>, halved while the lines would be closer
    /// than <see cref="MinLineSpacing"/> (they would merge into a grey area).
    /// </summary>
    private int LinesPerBeat(BarGrid grid)
    {
        double pps = PixelsPerSecond;
        int perBeat = Math.Max(1, (SelectedLineDivision?.Division ?? 4) / 4);
        while (perBeat > 1 && grid.BeatSeconds / perBeat * pps < MinLineSpacing) perBeat /= 2;
        return perBeat;
    }

    /// <summary>Lines per bar visible in the timeline: bars, beats and the thin lines (only bars when beats are too close).</summary>
    private int VisibleLinesPerBar(BarGrid grid) =>
        grid.BeatSeconds * PixelsPerSecond >= MinLineSpacing ? grid.BeatsPerBar * LinesPerBeat(grid) : 1;

    private void RefreshGridLines()
    {
        UpdateLoopDisplay(); // its text names bars while there are bar lines
        if (DisplayGrid is not { } grid)
        {
            BarLines = null;
            BeatLines = null;
            SubdivisionLines = null;
            BeatMarks = [];
            HasGridLines = false;
            return;
        }

        double seconds = _timelineSeconds;
        double pps = PixelsPerSecond;
        int perBeat = LinesPerBeat(grid);
        bool beatLines = grid.BeatSeconds * pps >= MinLineSpacing;
        int perBar = grid.BeatsPerBar * perBeat;
        double step = grid.BeatSeconds / perBeat;
        int steps = grid.BarCount(seconds) * perBar;

        // Numbers as in the drum editor: beats only when there is room, bars thinned out when they are narrow.
        bool beatLabels = grid.BeatSeconds * pps >= MinLabelSpacing;
        int barLabelEvery = Math.Max(1, (int)Math.Ceiling(MinLabelSpacing / (grid.BarSeconds * pps)));

        var marks = new List<BeatMark>();
        var bars = new StreamGeometry();
        var beats = new StreamGeometry();
        var subdivisions = new StreamGeometry();
        using (var barContext = bars.Open())
        using (var beatContext = beats.Open())
        using (var subdivisionContext = subdivisions.Open())
        {
            for (int i = 0; i <= steps; i++)
            {
                double t = grid.OffsetSeconds + i * step;
                if (t < 0 || t > seconds) continue;
                double x = t * pps;
                int bar = i / perBar, beat = i % perBar / perBeat;
                if (i % perBar == 0)
                {
                    AddLine(barContext, x, 0);
                    if (bar % barLabelEvery == 0)
                        marks.Add(new BeatMark(x, (bar + 1).ToString(CultureInfo.InvariantCulture), IsBar: true));
                }
                else if (i % perBeat == 0)
                {
                    if (beatLines) AddLine(beatContext, x, 0);
                    if (beatLabels) marks.Add(new BeatMark(x, $"{bar + 1}.{beat + 1}", IsBar: false));
                }
                else AddLine(subdivisionContext, x, SubdivisionTop);
            }
        }
        bars.Freeze();
        beats.Freeze();
        subdivisions.Freeze();
        BarLines = bars;
        BeatLines = beats;
        SubdivisionLines = perBeat > 1 ? subdivisions : null;
        BeatMarks = marks;
        HasGridLines = true;

        static void AddLine(StreamGeometryContext context, double x, double top)
        {
            context.BeginFigure(new Point(x, top), isFilled: false, isClosed: false);
            context.LineTo(new Point(x, TimelineHeight), isStroked: true, isSmoothJoin: false);
        }
    }

    /// <summary>Chord sheet of the current take, one entry per bar.</summary>
    public ObservableCollection<BarRowViewModel> Bars { get; } = [];

    /// <summary>True while the chord sheet has bars to show.</summary>
    [ObservableProperty] private bool _hasBars;

    /// <summary>Tempo of the shown chord sheet, e.g. "♩=120  4/4  ・ 8 小節".</summary>
    [ObservableProperty] private string _barSheetInfo = "";

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
    private async Task StartRecordingAsync()
    {
        StopPlayback();
        _barGrid = null;
        long? barOneTimestamp = null;
        bool withDrums = (PlayDrumsWhileRecording || SyncToBars) && _tempo is not null;
        bool withBars = SyncToBars && withDrums;
        if (withDrums)
        {
            // Drums / metronome first: the recording then starts right after bar 1 began.
            StatusText = "ドラム（メトロノーム）を準備中…";
            barOneTimestamp = await _tempo!.StartAsync();
            if (barOneTimestamp is null)
            {
                StatusText = "ドラム（メトロノーム）を開始できなかったため、録音しませんでした。";
                _dialogs.ShowError("ドラム編集のパターンを再生できませんでした。\nドラム編集画面で音源の準備ができているか確認してください。");
                return;
            }
            _tempoStarted = true;
        }

        try
        {
            var path = NewRecordingPath();
            if (UseAsio) StartAsioRecording(path);
            else _recorder.StartFromDevice(SelectedDeviceIndex, path);
            _recordingClock.Restart();
            if (withBars) _barGrid = NewBarGrid(barOneTimestamp!.Value, Stopwatch.GetTimestamp());
            RefreshGridLines(); // lines of this take (bar 1 = first bar of the drums)
            IsRecording = true;
            // A new take starts a new song: the capo suggestions use only the chords played from here on.
            ResetChordHistory();
            StatusText = withBars
                ? $"録音中…（{TempoText} のドラムに合わせて弾いてください。1 小節目はドラムの先頭です）"
                : withDrums
                    ? $"録音中…（{TempoText} のドラムを鳴らしています）"
                    : "録音中…（エフェクトをかける前のドライ音を録音しています）";
        }
        catch (Exception ex)
        {
            StopTempo();
            _barGrid = null;
            RefreshGridLines();
            _dialogs.ShowError($"録音を開始できませんでした。\n{ex.Message}");
        }
    }

    /// <summary>
    /// Bars of a recording that started at <paramref name="recordingTimestamp"/> while bar 1 began rendering at
    /// <paramref name="barOneTimestamp"/>. Bar 1 is heard after the drum output latency, so it is that much later
    /// in the recording; the time between the two starts moves it earlier.
    /// </summary>
    private BarGrid NewBarGrid(long barOneTimestamp, long recordingTimestamp)
    {
        double startGap = (recordingTimestamp - barOneTimestamp) / (double)Stopwatch.Frequency;
        return new BarGrid(_tempo!.Bpm, _tempo.BeatsPerBar, _tempo.OutputLatencySeconds - startGap);
    }

    /// <summary>Stops the drums / metronome if this page started them.</summary>
    private void StopTempo()
    {
        if (!_tempoStarted) return;
        _tempoStarted = false;
        _tempo?.Stop();
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
        else await StartRecordingAsync();
    }

    /// <summary>Clears the result (and the reference labels) so the next take starts from an empty timeline.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Clear()
    {
        UnloadPlayback();
        _analysis = null;
        _barGrid = null;
        _history.Reset(); // the capo suggestions start over as well
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
            StopTempo();
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
            StopTempo();
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

    private Task AnalyzeOpenedFileAsync(string path)
    {
        _barGrid = null; // the tempo of a file is not known (no beat detection yet)
        return AnalyzeFileAsync(path, UseSourceSeparation && IsSeparationAvailable ? SelectedStem.Stem : null);
    }

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
            _key = _keyEstimator.Estimate(analysis); // only the final result decides the key
            _keyDecided = true;
            // Only finished takes go into the history (intermediate results would count chords twice).
            _history.AddRange(_simplifier.SimplifyTimeline(analysis.Chords, ShownLevel, alsoShown: AlsoShownTypes).Select(c => c.Chord));
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
            var cts = Interlocked.Exchange(ref _analysisCts, null);
            cts?.Dispose();
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
        ResetLoop();
        SetPlaying(false);
    }

    private void UnloadPlayback()
    {
        _player.Unload();
        HasPlayback = false;
        ResetLoop();
        SetPlaying(false);
    }

    // ---------- loop (dragged on the ruler) ----------

    private double _loopStart, _loopEnd; // kept while the loop is switched off

    /// <summary>True while a loop range is chosen (shown as a band in the timeline).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearLoopCommand))]
    private bool _hasLoop;

    /// <summary>When true, playback repeats the loop range; switching off keeps the range.</summary>
    [ObservableProperty] private bool _isLoopEnabled;

    [ObservableProperty] private double _loopLeft;
    [ObservableProperty] private double _loopWidth;

    /// <summary>"0:12 – 0:20" (with bars "小節 3 – 6"); a hint while no loop is chosen.</summary>
    [ObservableProperty] private string _loopText = "ルーラーをドラッグして区間を指定";

    partial void OnIsLoopEnabledChanged(bool value) => ApplyLoopToPlayer();

    /// <summary>
    /// Time under the pointer for the loop ends: with bar lines the nearest line visible in the timeline (bar, beat or
    /// thin line of 線の細かさ, so it follows the zoom), otherwise as it is; always inside the audio.
    /// </summary>
    public double SnapLoopTime(double seconds)
    {
        if (DisplayGrid is { } grid) seconds = grid.NearestLine(seconds, VisibleLinesPerBar(grid));
        return Math.Clamp(seconds, 0, _player.DurationSeconds);
    }

    /// <summary>Shows the range being dragged without touching the playback (no device restarts while dragging).</summary>
    public void PreviewLoop(double a, double b)
    {
        if (!HasPlayback) return;
        (_loopStart, _loopEnd) = (Math.Min(a, b), Math.Max(a, b));
        HasLoop = _loopEnd - _loopStart >= AudioPlayer.MinLoopSeconds;
        UpdateLoopDisplay();
    }

    /// <summary>Sets the loop range (in either order) and switches the loop on; too short a range clears it.</summary>
    public void SetLoop(double a, double b)
    {
        if (!HasPlayback) return;
        PreviewLoop(a, b);
        if (!HasLoop)
        {
            ResetLoop();
            return;
        }
        if (IsLoopEnabled) ApplyLoopToPlayer();
        else IsLoopEnabled = true; // applied by OnIsLoopEnabledChanged
    }

    /// <summary>Start / end of the chosen loop in seconds; null without a loop (for dragging its ends).</summary>
    public (double Start, double End)? LoopRange => HasLoop ? (_loopStart, _loopEnd) : null;

    [RelayCommand(CanExecute = nameof(HasLoop))]
    private void ClearLoop() => ResetLoop();

    private void ResetLoop()
    {
        _loopStart = _loopEnd = 0;
        HasLoop = false;
        IsLoopEnabled = false;
        ApplyLoopToPlayer();
        UpdateLoopDisplay();
    }

    private void ApplyLoopToPlayer()
    {
        if (!_player.IsLoaded) return;
        if (IsLoopEnabled && HasLoop) _player.SetLoop(_loopStart, _loopEnd);
        else _player.ClearLoop();
        UpdatePlaybackPosition();
    }

    private void UpdateLoopDisplay()
    {
        double pps = PixelsPerSecond;
        LoopLeft = _loopStart * pps;
        LoopWidth = HasLoop ? (_loopEnd - _loopStart) * pps : 0;
        LoopText = !HasLoop ? "ルーラーをドラッグして区間を指定"
            : DisplayGrid is { } grid
                ? $"小節 {grid.BarIndexAt(_loopStart + 1e-6) + 1} – {grid.BarIndexAt(_loopEnd - 1e-6) + 1}（{FormatTime(_loopStart)} – {FormatTime(_loopEnd)}）"
                : $"{FormatTime(_loopStart)} – {FormatTime(_loopEnd)}";
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
        PlaybackLeft = position * PixelsPerSecond;
        PlaybackTimeText = HasPlayback ? $"{FormatTime(position)} / {FormatTime(_player.DurationSeconds)}" : "";
        UpdateCurrentChord();
        UpdateNowPlaying(position);
    }

    private static string FormatTime(double seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss", CultureInfo.InvariantCulture);

    // ---------- display (stage 7) ----------

    private void RefreshDisplay()
    {
        Chords.Clear();
        double seconds = _analysis?.DurationSeconds ?? 0;
        PlayheadLeft = seconds * PixelsPerSecond;
        // The empty timeline fills the screen: at least one screen of seconds.
        double minSeconds = Math.Max(MinTimelineSeconds, SelectedTimelineZoom?.Seconds ?? 0);
        int tickCount = (int)Math.Ceiling(Math.Max(minSeconds, seconds + 2));
        TimelineWidth = tickCount * PixelsPerSecond;
        _timelineSeconds = tickCount;
        RefreshGridLines();
        int labelEvery = RulerLabelSeconds;
        Ticks = Enumerable.Range(0, tickCount)
            .Where(s => s % labelEvery == 0)
            .Select(s => new TimelineTick(s * PixelsPerSecond, $"{s / 60:00}:{s % 60:00}"))
            .ToList();

        if (_analysis is null)
        {
            _key = null;
            _keyDecided = false;
            _soundingChords = [];
            _currentShown = null;
            _currentChord = null;
            _scaleChosenByUser = false; // a new song: the scale follows its key again
            UpdateFretboard();
            UpdateChordForm();
            UpdateKeyBadge();
            UpdateCapoSuggestions();
            RefreshBars([], null);
            UpdatePlaceholder();
            return;
        }

        // N.C. (no chord) is not shown; in the timeline its time is left empty.
        // Shown chords are the sounding ones after transposition; with a capo the shape is shown as well.
        var key = DisplayKey;
        var soundingChords = new List<DisplayedChord>();
        foreach (var detected in _simplifier.SimplifyTimeline(_analysis.Chords, ShownLevel, alsoShown: AlsoShownTypes).Where(c => !c.Chord.IsNoChord))
        {
            var sounding = detected with { Chord = Transposer.Transpose(detected.Chord, Transpose) };
            soundingChords.Add(sounding);
            string? shape = Capo > 0 ? NoteSpeller.SharpChordName(Transposer.Shape(sounding.Chord, Capo)) : null;
            var (main, sub) = Labels(sounding.Chord, key);
            Chords.Add(new ChordRowViewModel(sounding, main, sub, shape, NotesIn(detected, key), PixelsPerSecond));
        }
        UpdateKeyBadge();
        UpdateCapoSuggestions();
        RefreshBars(soundingChords, key);
        _soundingChords = soundingChords;
        _currentShown = ShownAtPlayback(); // the transposition may have changed it
        _currentChord = _currentShown?.Chord;
        UpdateFretboard();
        UpdateChordForm();

        UpdatePlaceholder();
        SummaryText = $"{_analysis.DurationSeconds:0.0} 秒 / 検出した音 {_analysis.Notes.Count} 個 / コード {Chords.Count} 個";
    }

    /// <summary>
    /// Chord sheet: one entry per bar of <see cref="_barGrid"/>. A bar that repeats the previous one shows "%".
    /// </summary>
    /// <param name="sounding">Shown chords after transposition.</param>
    private void RefreshBars(IReadOnlyList<DisplayedChord> sounding, MusicKey? key)
    {
        Bars.Clear();
        if (_barGrid is not { } grid || _analysis is null)
        {
            BarSheetInfo = "";
            HasBars = false;
            return;
        }

        double seconds = _analysis.DurationSeconds;

        string? previous = null;
        foreach (var bar in _barQuantizer.Quantize(sounding, grid, seconds))
        {
            IReadOnlyList<BarSlotViewModel> slots;
            string signature, detail;
            if (bar.IsEmpty)
            {
                signature = ChordResult.NoChordName;
                slots = [new BarSlotViewModel(signature, "", "")];
                detail = signature;
            }
            else
            {
                var named = bar.Chords.Select(c =>
                {
                    var (main, sub) = Labels(c, key);
                    return new BarSlotViewModel(main, sub,
                        Capo > 0 ? $"({NoteSpeller.SharpChordName(Transposer.Shape(c, Capo))})" : "");
                }).ToList();
                signature = string.Join(" ", named.Select(n => n.Name));
                string subs = string.Join(" ", named.Select(n => n.Sub));
                detail = string.IsNullOrWhiteSpace(subs) ? signature : $"{signature}（{subs}）";
                slots = signature == previous ? [new BarSlotViewModel("%", "", "")] : named;
            }
            Bars.Add(new BarRowViewModel(bar.Number, bar.StartSeconds, slots,
                $"{bar.Number} 小節目  {FormatTime(bar.StartSeconds)} から\n{detail}"));
            previous = signature;
        }

        HasBars = Bars.Count > 0;
        BarSheetInfo = $"♩={grid.Bpm:0}  {grid.BeatsPerBar}/4  ・ {Bars.Count} 小節";
    }

    /// <summary>The estimated key moved by the transposition; null while undecided (names then use #).</summary>
    private MusicKey? DisplayKey => _key?.Key.Transpose(Transpose);

    /// <summary>Shows the key once the whole performance is analysed; intermediate results show none.</summary>
    private void UpdateKeyBadge()
    {
        OnPropertyChanged(nameof(DegreeHint)); // degrees become available once the key is decided
        IsKeyVisible = _keyDecided;
        if (_key is null || DisplayKey is not { } key)
        {
            KeyName = "-";
            KeyDetail = "キーを判定できませんでした";
            return;
        }

        bool estimated = _key.ModeFromProfile;
        KeyName = (estimated ? "≈ " : "") + key.Name;
        KeyDetail = $"{key.JapaneseName}{(estimated ? "（推定）" : "")} ・ 平行調 {key.Relative.Name}";
    }

    // ---------- degrees ----------

    /// <summary>When true, the degree of the estimated key is shown below each chord name (timeline and chord sheet).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DegreeHint))]
    private bool _showDegrees;

    partial void OnShowDegreesChanged(bool value) => RefreshDisplay();

    /// <summary>Which note is Ⅰ in a minor key; the relative major (default) or the minor tonic.</summary>
    public IReadOnlyList<MinorDegreeBaseOption> MinorDegreeBases { get; } =
    [
        new(MinorDegreeBase.RelativeMajor, "平行調のメジャー（Am = Ⅵm、C = Ⅰ）"),
        new(MinorDegreeBase.MinorTonic, "マイナーの主音（Am = Ⅰm、C = ♭Ⅲ）"),
    ];

    [ObservableProperty] private MinorDegreeBaseOption? _selectedMinorDegreeBase;

    partial void OnSelectedMinorDegreeBaseChanged(MinorDegreeBaseOption? value) => RefreshDisplay();

    /// <summary>Note under the degree settings while degrees are wanted but the key is not decided yet.</summary>
    public string DegreeHint => ShowDegrees && DisplayKey is null
        ? (_keyDecided ? "キーを判定できなかったため、コード名で表示しています" : "キーが決まると（解析が終わると）ディグリーを表示します")
        : "";

    /// <summary>
    /// Label of a sounding chord: its name and the degree below it (empty when degrees are off or the key is not
    /// decided yet).
    /// </summary>
    private (string Main, string Sub) Labels(ChordResult sounding, MusicKey? key)
    {
        string name = NoteSpeller.ChordName(sounding, key);
        if (!ShowDegrees || key is not { } k) return (name, "");
        return (name, DegreeNamer.Degree(sounding, k, SelectedMinorDegreeBase?.Base ?? MinorDegreeBase.RelativeMajor));
    }

    partial void OnTransposeChanged(int value)
    {
        int clamped = Transposer.ClampTranspose(value);
        if (clamped != value) { Transpose = clamped; return; }
        RefreshDisplay();
    }

    /// <summary>
    /// Any capo change (selector or suggestion) sets the transposition to +capo (capo 2 → +2).
    /// </summary>
    partial void OnCapoChanged(int value)
    {
        int clamped = Transposer.ClampCapo(value);
        if (clamped != value) { Capo = clamped; return; }
        int transpose = Transposer.ClampTranspose(clamped);
        if (Transpose != transpose) { Transpose = transpose; return; } // OnTransposeChanged refreshes
        RefreshDisplay();
    }

    /// <summary>
    /// Applies a suggested capo position and sets the transposition to +capo (capo 2 → +2), as if the capo
    /// raised the sound. Transpose is set here too so that clicking the current capo again resets it.
    /// The parameter is the clicked row (a reference type): with an int parameter the command reports
    /// "cannot execute" while WPF still passes null, and the button then stays disabled.
    /// </summary>
    [RelayCommand]
    private void ApplyCapo(CapoSuggestionRow? row)
    {
        if (row is null) return;
        int clamped = Transposer.ClampCapo(row.Capo);
        Transpose = Transposer.ClampTranspose(clamped);
        Capo = clamped;
    }

    /// <summary>
    /// Forgets the played chords; suggestions go back to the key's representative chords.
    /// Called when a recording starts (there is no button: クリア also forgets them).
    /// </summary>
    private void ResetChordHistory()
    {
        _history.Reset();
        UpdateCapoSuggestions();
    }

    private void UpdateCapoSuggestions()
    {
        var (source, suggestions) = _capoAdvisor.Suggest(_history, _key?.Key, Transpose);
        CapoSuggestionSourceText = source switch
        {
            CapoSuggestionSource.History => $"弾いたコードから（{_history.DistinctCount} 種類）",
            CapoSuggestionSource.Key when DisplayKey is { } key => $"キー {key.Name} の代表コードから",
            _ => "-",
        };
        CapoSuggestions = [.. suggestions.Select(s => new CapoSuggestionRow(
            s.Capo,
            s.Capo == 0 ? "カポなし" : $"カポ {s.Capo}",
            string.Join("  ", s.Shapes),
            $"{s.Score:0.0} 点",
            s.Capo == Capo))];
    }

    // ---------- scale on the fretboard ----------

    /// <summary>Shown chords after transposition (N.C. left out), for the chord under the playback position.</summary>
    private IReadOnlyList<DisplayedChord> _soundingChords = [];

    /// <summary>The shown chord (time range + sounding chord) at the playback position; null when none.</summary>
    private DisplayedChord? _currentShown;

    /// <summary>The sounding chord at the playback position; null when none (or nothing is loaded).</summary>
    private ChordResult? _currentChord;

    /// <summary>True once the scale was picked by hand for this song; until then it follows the key.</summary>
    private bool _scaleChosenByUser;

    private bool _choosingScaleAutomatically;

    /// <summary>Frets shown in the scale view (0–12: one octave, wide enough for the note names).</summary>
    public const int ScaleFretCount = 12;

    public IReadOnlyList<ScaleOption> ScaleOptions { get; } =
        [.. Enum.GetValues<ScaleType>().Select(t => new ScaleOption(t, Scale.JapaneseName(t)))];

    [ObservableProperty] private ScaleOption? _selectedScale;

    partial void OnSelectedScaleChanged(ScaleOption? value)
    {
        if (!_choosingScaleAutomatically) _scaleChosenByUser = true;
        UpdateFretboard();
    }

    public IReadOnlyList<FretLabelOption> FretLabels { get; } =
    [
        new(FretLabelMode.NoteName, "音名（C, D, E …）"),
        new(FretLabelMode.Interval, "度数（R, ♭3, 5 …）"),
    ];

    [ObservableProperty] private FretLabelOption? _selectedFretLabel;

    partial void OnSelectedFretLabelChanged(FretLabelOption? value)
    {
        UpdateFretboard();
        UpdateChordForm();
    }

    /// <summary>Dots on the fretboard: notes of the scale and of the chord at the playback position.</summary>
    [ObservableProperty] private IReadOnlyList<FretMarkViewModel> _fretboardMarks = [];

    /// <summary>True when a key is known and the fretboard shows its scale.</summary>
    [ObservableProperty] private bool _hasFretboard;

    /// <summary>Scale and key, e.g. "A マイナー・ペンタトニック（KEY Am）".</summary>
    [ObservableProperty] private string _scaleTitle = "";

    /// <summary>Shown instead of the fretboard while no key is known.</summary>
    [ObservableProperty] private string _fretboardHint = "";

    /// <summary>Finds the chord at the playback position and redraws the fretboard only when it changed.</summary>
    private void UpdateCurrentChord()
    {
        var shown = ShownAtPlayback();
        if (shown == _currentShown) return;
        _currentShown = shown;
        _currentChord = shown?.Chord;
        UpdateFretboard();
        UpdateChordForm();
    }

    private DisplayedChord? ShownAtPlayback()
    {
        if (!HasPlayback) return null;
        double position = _player.PositionSeconds;
        return _soundingChords.FirstOrDefault(c => c.StartSeconds <= position && position < c.EndSeconds);
    }

    private void UpdateFretboard()
    {
        if (DisplayKey is not { } key)
        {
            HasFretboard = false;
            FretboardMarks = [];
            ScaleTitle = "";
            FretboardHint = _keyDecided
                ? "キーを判定できなかったため、スケールを表示できません"
                : "解析が終わる（キーが決まる）と、キーで使える音を指板に表示します";
            return;
        }

        if (!_scaleChosenByUser)
        {
            var wanted = Scale.DefaultFor(key);
            if (SelectedScale?.Type != wanted)
            {
                _choosingScaleAutomatically = true;
                try { SelectedScale = ScaleOptions.First(o => o.Type == wanted); } // calls back into UpdateFretboard
                finally { _choosingScaleAutomatically = false; }
                return;
            }
        }

        var type = SelectedScale?.Type ?? Scale.DefaultFor(key);
        int root = Scale.RootFor(key, type);
        bool intervals = SelectedFretLabel?.Mode == FretLabelMode.Interval;
        var chord = _currentChord is { IsNoChord: false } c ? c : null;
        FretboardMarks = [.. Fretboard.Marks(Scale.PitchClassesOf(key, type), root, chord, Capo, ScaleFretCount)
            .Select(m => new FretMarkViewModel(m.StringIndex, m.Fret,
                intervals ? Scale.IntervalName(m.PitchClass - root) : NoteSpeller.Name(m.PitchClass, key), m.Kind))];
        ScaleTitle = $"{NoteSpeller.Name(root, key)} {Scale.JapaneseName(type)}（KEY {key.Name}{(_key?.ModeFromProfile == true ? " 推定" : "")}）";
        FretboardHint = "";
        HasFretboard = true;
    }

    /// <summary>Notes under a chord, moved by the transposition and spelled for the key.</summary>
    private string NotesIn(DisplayedChord shown, MusicKey? key) =>
        string.Join(" ", PitchesIn(shown).Select(p => NoteSpeller.MidiName(p, key)));

    /// <summary>MIDI pitches heard during a chord, moved by the transposition, low to high, each once.</summary>
    private IEnumerable<int> PitchesIn(DisplayedChord shown) =>
        (_analysis?.Notes ?? [])
            .Where(n => n.StartSeconds < shown.EndSeconds && n.EndSeconds > shown.StartSeconds)
            .Select(n => n.Pitch + Transpose)
            .Distinct()
            .Order();

    // ---------- fretboard panel (指板): open / tabs / chord forms ----------

    /// <summary>True while the fretboard panel is open (fixed at the bottom; the timeline gives way as it grows).</summary>
    [ObservableProperty] private bool _isFretboardOpen;

    /// <summary>
    /// True while the timeline is low (set by the view from its height): the detected-notes row and the start times
    /// are hidden and the chord text is smaller.
    /// </summary>
    [ObservableProperty] private bool _isTimelineCompact;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChordFormTab), nameof(IsScaleTab))]
    private FretboardTab _selectedFretboardTab;

    public const double DefaultFretboardHeight = 218;
    public const double MinFretboardHeight = 200;
    /// <summary>Absolute upper bound; the real limit comes from the space on screen (see <see cref="ResizeFretboard"/>).</summary>
    private const double AbsoluteMaxFretboardHeight = 1200;
    /// <summary>How far the top edge must be pulled past the timeline before the timeline closes.</summary>
    private const double HideTimelineOvershoot = 40;

    /// <summary>Height of the neck in the fretboard panel; changed by dragging the panel's top edge.</summary>
    [ObservableProperty] private double _fretboardHeight = DefaultFretboardHeight;

    partial void OnFretboardHeightChanged(double value)
    {
        double clamped = double.IsFinite(value) ? Math.Clamp(value, MinFretboardHeight, AbsoluteMaxFretboardHeight) : DefaultFretboardHeight;
        if (clamped != value) FretboardHeight = clamped;
    }

    /// <summary>True when the panel was enlarged over the timeline (コード進行 is closed and the panel shown large).</summary>
    [ObservableProperty] private bool _isTimelineHidden;

    partial void OnIsFretboardOpenChanged(bool value)
    {
        if (!value) IsTimelineHidden = false;
    }

    /// <summary>
    /// Height asked for by dragging the panel's top edge (the panel sits at the bottom, so it grows upwards).
    /// <paramref name="maxWithTimeline"/> is the largest neck that fits below the timeline, <paramref name="maxLarge"/>
    /// the largest once the timeline is closed. Pulling past the first by <see cref="HideTimelineOvershoot"/> closes
    /// the timeline; pulling back below it opens the timeline again.
    /// </summary>
    public void ResizeFretboard(double requested, double maxWithTimeline, double maxLarge)
    {
        if (!double.IsFinite(requested)) return;
        if (!IsTimelineHidden && requested >= maxWithTimeline + HideTimelineOvershoot) IsTimelineHidden = true;
        else if (IsTimelineHidden && requested < maxWithTimeline) IsTimelineHidden = false;
        FretboardHeight = Math.Min(requested, LimitFor(maxWithTimeline, maxLarge));
    }

    /// <summary>Shrinks the neck when it no longer fits (smaller window, saved height); never opens or closes the timeline.</summary>
    public void ClampFretboard(double maxWithTimeline, double maxLarge)
    {
        double limit = LimitFor(maxWithTimeline, maxLarge);
        if (FretboardHeight > limit) FretboardHeight = limit;
    }

    private double LimitFor(double maxWithTimeline, double maxLarge) =>
        Math.Max(MinFretboardHeight, IsTimelineHidden ? maxLarge : maxWithTimeline);

    /// <summary>Opens the timeline again and gives the panel its default size.</summary>
    [RelayCommand]
    private void ShowTimeline()
    {
        IsTimelineHidden = false;
        FretboardHeight = DefaultFretboardHeight;
    }

    public bool IsChordFormTab
    {
        get => SelectedFretboardTab == FretboardTab.ChordForm;
        set { if (value) SelectedFretboardTab = FretboardTab.ChordForm; }
    }

    public bool IsScaleTab
    {
        get => SelectedFretboardTab == FretboardTab.Scale;
        set { if (value) SelectedFretboardTab = FretboardTab.Scale; }
    }

    /// <summary>Forms of the shape at the playback position, easiest first, and the one shown.</summary>
    private IReadOnlyList<ChordForm> _forms = [];
    private int _formIndex;
    private ChordResult? _formsFor;

    /// <summary>True when a chord is under the playback position and a form is shown.</summary>
    [ObservableProperty] private bool _hasChordForm;

    /// <summary>Shown instead of the chord form when there is none.</summary>
    [ObservableProperty] private string _chordFormHint = "";

    /// <summary>Name of the shape to fret, e.g. "Asus4".</summary>
    [ObservableProperty] private string _chordFormName = "";

    /// <summary>With a capo: "カポ2・実音 Bsus4"; otherwise empty.</summary>
    [ObservableProperty] private string _chordFormSub = "";

    /// <summary>The form as TAB text, e.g. "x02230".</summary>
    [ObservableProperty] private string _chordFormText = "";

    [ObservableProperty] private ChordForm? _currentChordForm;

    /// <summary>Fretted notes of the form for the small diagram (frets without the capo).</summary>
    [ObservableProperty] private IReadOnlyList<FretMarkViewModel> _chordDiagramDots = [];

    /// <summary>Dots on the neck: the form and the chord's notes elsewhere.</summary>
    [ObservableProperty] private IReadOnlyList<FretMarkViewModel> _chordFormMarks = [];

    [ObservableProperty] private IReadOnlyList<int> _chordFormMutedStrings = [];

    /// <summary>Frets shown for the chord form: 0–12 as in the scale view, more when the form goes higher.</summary>
    [ObservableProperty] private int _chordFormFretCount = ScaleFretCount;

    /// <summary>"別のフォーム（2/3）".</summary>
    [ObservableProperty] private string _alternateFormText = "別のフォーム";

    /// <summary>Notes detected during the current chord; notes outside the chord are drawn dashed.</summary>
    [ObservableProperty] private IReadOnlyList<DetectedNoteChip> _detectedNotes = [];

    [ObservableProperty] private bool _isNowPlaying;
    [ObservableProperty] private string _nowPlayingState = "";
    [ObservableProperty] private string _nowPlayingChord = "";
    [ObservableProperty] private string _nowPlayingDegree = "";
    [ObservableProperty] private string _nowPlayingTime = "";

    [RelayCommand(CanExecute = nameof(HasAlternateForms))]
    private void NextChordForm()
    {
        if (_forms.Count == 0) return;
        _formIndex = (_formIndex + 1) % _forms.Count;
        UpdateChordForm();
    }

    private bool HasAlternateForms() => _forms.Count > 1;

    /// <summary>Rebuilds the chord-form tab for the chord at the playback position (called when it, the capo or the transposition changes).</summary>
    private void UpdateChordForm()
    {
        UpdateNowPlaying(_player.PositionSeconds);
        var chord = _currentChord is { IsNoChord: false } c ? c with { Bass = null } : null;
        if (chord is null)
        {
            _forms = [];
            _formsFor = null;
            HasChordForm = false;
            ChordFormHint = HasPlayback
                ? "再生位置にコードがありません（タイムラインのコードをクリックすると、そのフォームを表示します）"
                : "音声を開くか録音して解析すると、再生位置のコードの押さえ方を表示します";
            ChordFormName = ChordFormSub = ChordFormText = "";
            CurrentChordForm = null;
            ChordDiagramDots = ChordFormMarks = [];
            ChordFormMutedStrings = [];
            DetectedNotes = [];
            NextChordFormCommand.NotifyCanExecuteChanged();
            return;
        }

        var key = DisplayKey;
        var shape = Transposer.Shape(chord, Capo); // with a capo the form is the shape to fret
        if (shape != _formsFor)
        {
            _forms = ChordForms.For(shape);
            _formIndex = 0;
            _formsFor = shape;
        }
        NextChordFormCommand.NotifyCanExecuteChanged();

        ChordFormName = Capo > 0 ? NoteSpeller.SharpChordName(shape) : NoteSpeller.ChordName(chord, key);
        ChordFormSub = Capo > 0 ? $"カポ{Capo}・実音 {NoteSpeller.ChordName(chord, key)}" : "";
        var tones = Fretboard.ChordTones(chord);
        DetectedNotes = _currentShown is { } shown
            ? [.. PitchesIn(shown).Select(p => new DetectedNoteChip(NoteSpeller.MidiName(p, key), tones.Contains(PitchClasses.Of(p))))]
            : [];

        if (_forms.Count == 0)
        {
            HasChordForm = false;
            ChordFormHint = $"{ChordFormName} のフォームはまだ用意していません";
            ChordFormText = "";
            CurrentChordForm = null;
            ChordDiagramDots = ChordFormMarks = [];
            ChordFormMutedStrings = [];
            return;
        }

        var form = _forms[_formIndex];
        // 0–12 as in the scale view; more only when the form goes higher.
        int fretCount = Math.Min(Fretboard.DefaultFretCount, Math.Max(ScaleFretCount, Capo + form.MaxFret + 1));
        string Interval(int pc) => ChordToneName(PitchClasses.Of(pc - chord.Root), chord.Type!);
        bool noteNames = SelectedFretLabel?.Mode != FretLabelMode.Interval;
        string Label(int pc) => noteNames ? NoteSpeller.Name(pc, key) : Interval(pc);
        var marks = Fretboard.ChordFormMarks(form, chord, Capo, fretCount);
        ChordFormMarks = [.. marks.Select(m => new FretMarkViewModel(m.StringIndex, m.Fret, Label(m.PitchClass), m.Kind))];
        // The small diagram keeps the intervals (R, 3, 5 …), as on chord charts.
        ChordDiagramDots = [.. marks.Where(m => m.Kind is FretMarkKind.FormRoot or FretMarkKind.FormTone)
            .Select(m => new FretMarkViewModel(m.StringIndex, m.Fret - Capo, Interval(m.PitchClass), m.Kind))];
        ChordFormMutedStrings = [.. Enumerable.Range(0, 6).Where(s => form.FretOn(s) is null)];
        ChordFormFretCount = fretCount;
        CurrentChordForm = form;
        ChordFormText = form.Text;
        AlternateFormText = $"別のフォーム（{_formIndex + 1}/{_forms.Count}）";
        ChordFormHint = "";
        HasChordForm = true;
    }

    /// <summary>Interval label of a chord tone: as <see cref="Scale.IntervalName"/>, but ♯5 for aug and 9 for add9.</summary>
    private static string ChordToneName(int interval, ChordType type) => interval switch
    {
        8 when !type.Intervals.Contains(7) => "♯5",
        2 when type.Intervals.Contains(4) => "9",
        _ => Scale.IntervalName(interval),
    };

    /// <summary>"● 再生中 Asus4 Ⅴsus4 ・ 0:05.74" in the panel header.</summary>
    private void UpdateNowPlaying(double position)
    {
        IsNowPlaying = IsPlaying;
        NowPlayingState = !HasPlayback ? "" : IsPlaying ? "再生中" : "停止中";
        NowPlayingTime = HasPlayback
            ? TimeSpan.FromSeconds(Math.Max(0, position)).ToString(@"m\:ss\.ff", CultureInfo.InvariantCulture)
            : "";
        var key = DisplayKey;
        var chord = _currentChord is { IsNoChord: false } c ? c : null;
        NowPlayingChord = chord is null ? (HasPlayback ? ChordResult.NoChordName : "") : NoteSpeller.ChordName(chord, key);
        NowPlayingDegree = chord is not null && key is { } k
            ? DegreeNamer.Degree(chord, k, SelectedMinorDegreeBase?.Base ?? MinorDegreeBase.RelativeMajor)
            : "";
    }

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
        // A change made just before closing is still waiting for the timer.
        if (_settingsSaveTimer.IsEnabled) { _settingsSaveTimer.Stop(); CurrentSettings().Save(); }
        _recorder.LevelChanged -= OnRecorderLevel;
        _asio.AudioReceived -= OnAsioAudio;
        if (_tempo is not null) _tempo.TempoChanged -= OnTempoChanged;
        StopTempo();
        // MainWindow owns the shared input and disposes it once on application close.
        _player.PlaybackEnded -= OnPlaybackEnded;
        _playbackTimer.Stop();
        _analysisCts?.Cancel();
        _recorder.Dispose();
        _player.Dispose();
    }
}
