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

namespace FXForge.Views;

// The original FFT/HPS tuner is retained in TunerAnalyzer.cs.
// Only the buffer dispatch is changed: FFT must not block the low-latency ASIO callback.
public partial class TunerPage : UserControl, IDisposable
{
    private readonly TunerAnalyzer _tuner = new();
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
        Loaded += (_, _) => { _listening = true; _silenceTimer.Start(); Draw(); };
        Unloaded += (_, _) => { _listening = false; _silenceTimer.Stop(); };
        _silenceTimer.Tick += (_, _) =>
        {
            if (Environment.TickCount64 - Interlocked.Read(ref _lastSignalTimestamp) > 1500)
                UpdateTuner(new TunerResult { hasSignal = false });
        };
        _ = Task.Run(AnalyzeInBackground);
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
        _silenceTimer.Stop();
        _stopping.Cancel();
        _queue.Writer.TryComplete();
    }
}
