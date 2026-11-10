using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FXForge.Desktop.Views;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FXForge.Views;

// The original FFT/HPS tuner is retained in TunerAnalyzer.cs.

// Only the buffer dispatch is changed: FFT must not block the low-latency ASIO callback.
public partial class TunerPage : UserControl, IDisposable
{
    private readonly TunerAnalyzer _tuner = new();
    // 6弦同時解析
    private readonly PolyphonicTunerAnalyzer _polyTuner = new();
    private readonly ObservableCollection<StringMeterRow> _stringMeters = new();

    // 起動時はOFF
    private volatile bool _polyEnabled;
    private readonly Channel<AudioChunk> _queue = Channel.CreateBounded<AudioChunk>(
        new BoundedChannelOptions(64)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    private readonly CancellationTokenSource _stopping = new();
    private readonly DispatcherTimer _silenceTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private double _cents;
    private long _lastSignalTimestamp;
    private volatile bool _listening;
    private bool _disposed;

    private sealed record AudioChunk(float[] Samples, int SampleRate, int Channel);

    public TunerPage()
    {
        InitializeComponent();

        // 6弦チューナー
        string[] notes = { "E2", "A2", "D3", "G3", "B3", "E4" };

        for (int i = 0; i < notes.Length; i++)
            _stringMeters.Add(new StringMeterRow(6 - i, notes[i]));

        StringMeterList.ItemsSource = _stringMeters;
        _polyTuner.ResultsReady += OnPolyResultsReady;
        //

        Loaded += (_, _) => { _listening = true; _silenceTimer.Start(); Draw(); };
        Unloaded += (_, _) => { _listening = false; _silenceTimer.Stop(); };
        _silenceTimer.Tick += (_, _) =>
        {
            if (Environment.TickCount64 - Interlocked.Read(ref _lastSignalTimestamp) > 1500)
                UpdateTuner(new TunerResult { hasSignal = false });
        };
        _ = Task.Run(AnalyzeInBackground);
    }

    private void PolyTunerToggle_Click(object sender, RoutedEventArgs e)
    {
        _polyEnabled = PolyTunerToggle.IsChecked == true;
        PolyTunerToggle.Content = _polyEnabled ? "ON" : "OFF";

        if (!_polyEnabled)
        {
            foreach (var row in _stringMeters)
                row.Apply(new StringTuningResult());
        }
    }

    private void OnPolyResultsReady(StringTuningResult[] results)
    {
        if (Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
            return;

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() => OnPolyResultsReady(results)),
                DispatcherPriority.Background);
            return;
        }

        // OFF中・画面非表示中・終了後の結果は表示しない
        if (_disposed || !_listening || !_polyEnabled)
            return;

        for (int i = 0; i < Math.Min(_stringMeters.Count, results.Length); i++)
            _stringMeters[i].Apply(results[i]);
    }

    // Samples are reused by CommonAudioInput after the callback ends. Copy once, enqueue,
    // and return promptly so the real-time audio output is not held by FFT processing.
    public void ReceiveAudio(float[] samples, int sampleRate, int inputChannel)
    {
        if (!_listening || _disposed || sampleRate <= 0 || samples.Length == 0) return;
        _queue.Writer.TryWrite(new AudioChunk((float[])samples.Clone(), sampleRate, inputChannel));
    }

    private async Task AnalyzeInBackground()
    {
        try
        {
            await foreach (var chunk in _queue.Reader.ReadAllAsync(_stopping.Token))
            {
                // 画面が表示中で、6弦解析がONの場合のみ音声を渡す
                if (!_disposed && _listening && _polyEnabled)
                    _polyTuner.AddSamples(chunk.Samples, chunk.SampleRate);

                var result = _tuner.AddSamples(chunk.Samples, chunk.SampleRate, chunk.Channel);
                if (result is not { hasSignal: true } || _disposed) continue;
                Interlocked.Exchange(ref _lastSignalTimestamp, Environment.TickCount64);
                if (!Dispatcher.HasShutdownStarted)
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_disposed && _listening) UpdateTuner(result);
                    }), DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Tuner analyzer: " + ex);
            if (!Dispatcher.HasShutdownStarted)
                _ = Dispatcher.BeginInvoke(new Action(() => TunerStatusText.Text = "解析エラー: " + ex.Message));
        }
    }

    public void UpdateTuner(TunerResult result)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() => UpdateTuner(result)));
            return;
        }
        if (_disposed) return;
        _cents = result.hasSignal && double.IsFinite(result.cents) ? Math.Clamp(result.cents, -50, 50) : 0;
        NoteText.Text = result.hasSignal ? result.note : "— —";
        FrequencyText.Text = result.hasSignal ? $"{result.frequency:0.0} Hz" : "— Hz";
        CentsText.Text = result.hasSignal ? $"{result.cents:+0.0;-0.0;0.0}" : "—";
        TunerStatusText.Text = result.hasSignal ? "入力検出中" : "入力待機中";
        Draw();
    }

    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(
        Window.GetWindow(this), "共通音声入力から音名・周波数・音程のずれをFFTで解析します。入力機器は右上の⚙から設定してください。", "チューナー");

    private void Meter_SizeChanged(object sender, SizeChangedEventArgs e) => Draw();

    private static void Label(Canvas canvas, string text, double x, double y, int size, Brush color)
    {
        var label = new TextBlock { Text = text, FontSize = size, Foreground = color };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        canvas.Children.Add(label);
    }

    private void Draw()
    {
        var c = MeterCanvas;
        if (c == null || c.ActualWidth < 10 || c.ActualHeight < 10) return;
        c.Children.Clear();
        double w = c.ActualWidth, h = c.ActualHeight, cx = w / 2, cy = h * .83;
        double r = Math.Min(w * .43, h * .66);
        var arc = new Polyline { Stroke = Brushes.White, StrokeThickness = 3 };
        for (int i = 0; i <= 100; i++)
        {
            double a = (200 + 140 * i / 100.0) * Math.PI / 180;
            arc.Points.Add(new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }
        c.Children.Add(arc);
        for (int i = 0; i <= 20; i++)
        {
            double a = (200 + 140 * i / 20.0) * Math.PI / 180;
            double inner = r - (i % 5 == 0 ? 42 : 24);
            c.Children.Add(new Line
            {
                X1 = cx + inner * Math.Cos(a), Y1 = cy + inner * Math.Sin(a),
                X2 = cx + r * Math.Cos(a), Y2 = cy + r * Math.Sin(a),
                Stroke = Brushes.White, StrokeThickness = i % 5 == 0 ? 5 : 3
            });
        }
        Label(c, "-50", cx - r - 18, cy - r * .34, 22, Brushes.White);
        Label(c, "-25", cx - r * .72 - 16, cy - r * .81 - 20, 22, Brushes.White);
        Label(c, "0", cx - 8, cy - r - 55, 24, Brushes.White);
        Label(c, "+25", cx + r * .64 - 10, cy - r * .81 - 20, 22, Brushes.White);
        Label(c, "+50", cx + r - 38, cy - r * .34, 22, Brushes.White);
        double angle = (270 + _cents * 1.4) * Math.PI / 180;
        Brush color = Math.Abs(_cents) <= 5 && NoteText.Text != "— —"
            ? Brushes.LimeGreen : new SolidColorBrush(Color.FromRgb(40, 184, 255));
        c.Children.Add(new Line
        {
            X1 = cx, Y1 = cy,
            X2 = cx + (r - 20) * Math.Cos(angle), Y2 = cy + (r - 20) * Math.Sin(angle),
            Stroke = color, StrokeThickness = 5
        });
        c.Children.Add(new Rectangle { Width = 18, Height = 18, Fill = color, Margin = new Thickness(cx - 9, cy - 9, 0, 0) });
        Label(c, "フラット", 28, h - 48, 23, Brushes.White);
        Label(c, "シャープ", Math.Max(30, w - 150), h - 48, 23, Brushes.White);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _listening = false;
        _polyEnabled = false;

        _polyTuner.ResultsReady -= OnPolyResultsReady;

        _silenceTimer.Stop();
        _stopping.Cancel();
        _queue.Writer.TryComplete();
    }
}
/// <summary>6行分のUI表示用データ。ItemsControlが自動で更新される。</summary>
public sealed class StringMeterRow : INotifyPropertyChanged
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(50, 211, 160));
    private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(40, 184, 255));
    private static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(130, 153, 171));
    private readonly string _targetNote;

    public string StringLabel { get; }
    public string SoundText { get; private set; } = "未検出";
    public string CentText { get; private set; } = "—";
    public double MarkerLeft { get; private set; } = 116; // 0centに相当
    public Visibility MarkerVisibility { get; private set; } = Visibility.Collapsed;
    public Brush Accent { get; private set; } = Gray;

    public event PropertyChangedEventHandler? PropertyChanged;

    public StringMeterRow(int number, string targetNote)
    {
        StringLabel = $"{number}弦";
        _targetNote = targetNote;
        SoundText = targetNote + " 未検出";
    }/// <summary>
    /// //////////////////////////
    /// </summary>
    /// <param name="result"></param>

    public void Apply(StringTuningResult result)
    {
        if (!result.IsDetected)
        {
            SoundText = _targetNote + " 未検出";
            CentText = "—";
            MarkerVisibility = Visibility.Collapsed;
            Accent = Gray;
        }
        else
        {
            double freq = result.Frequency!.Value;
            double cent = result.Cents!.Value;
            string detectedNote = NoteFromFrequency(freq);
            SoundText = $"{detectedNote} {freq:0.0}Hz";
            CentText = $"{cent:+0.0;-0.0;0.0}¢";
            // 240pxのCanvas上に8pxの針を移動。-50=左、0=中央、+50=右。
            MarkerLeft = 116 + Math.Clamp(cent, -50.0, 50.0) * 2.32;
            MarkerVisibility = Visibility.Visible;
            Accent = Math.Abs(cent) <= 5.0 ? Green : Blue;
        }
        Changed(nameof(SoundText));
        Changed(nameof(CentText));
        Changed(nameof(MarkerLeft));
        Changed(nameof(MarkerVisibility));
        Changed(nameof(Accent));
    }

    private static string NoteFromFrequency(double hz)
    {
        if (hz <= 0 || !double.IsFinite(hz)) return "—";
        string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        int midi = (int)Math.Round(69 + 12 * Math.Log2(hz / 440.0));
        int noteIndex = (midi % 12 + 12) % 12;
        return noteNames[noteIndex] + (midi / 12 - 1);
    }

    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

