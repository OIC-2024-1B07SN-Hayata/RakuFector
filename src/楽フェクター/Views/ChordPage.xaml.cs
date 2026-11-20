using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ChordDetection.App.Services;
using ChordDetection.App.ViewModels;
using ChordDetection.Core;
using ChordDetection.Core.Chords;
using ChordDetection.Core.Notes;
using ChordDetection.Core.Segmentation;
using ChordDetection.Infrastructure.Audio;
using ChordDetection.Infrastructure.BasicPitch;
using ChordDetection.Infrastructure.Chroma;
using ChordDetection.Infrastructure.Separation;
using CommonAudio;

namespace FXForge.Views
{

/// <summary>
/// Full chord detection workspace embedded into FX Forge. Uses the shell's one and only
/// CommonAudioInput, so tone/tuner/chord input do not open competing ASIO sessions.
/// </summary>
public partial class ChordPage : UserControl, IDisposable
{
    private IPitchModel? _model;
    private ISourceSeparator? _separator;
    private ChordDetectionViewModel? _viewModel;

    public bool IsRecording => _viewModel?.IsRecording == true;

    public ChordPage(CommonAudioInput audio, Func<bool> audioConnected, Func<int> sampleRate, ITempoSource? tempo = null)
    {
        InitializeComponent();
        Loaded += (_, _) => AttachTrackpad();
        Unloaded += (_, _) => DetachTrackpad();

        try
        {
            _model = new BasicPitchModel();
            _separator = DemucsOnnxSeparator.IsModelAvailable() ? new DemucsOnnxSeparator() : null;

            var catalog = ChordCatalog.Default;
            var classifier = new RuleBasedChordClassifier(catalog);
            var service = new ChordDetectionService(_model, classifier: classifier);
            var toneShapedService = new ChordDetectionService(
                _model,
                decoder: new NoteDecoder(NoteDecoderOptions.ToneShaped),
                segmenter: new NoteSegmenter(NoteSegmenterOptions.ToneShaped),
                classifier: classifier);

            _viewModel = new ChordDetectionViewModel(
                service, toneShapedService,
                new ChromaChordDetector(classifier),
                new AudioPreprocessor(),
                new ChordRecorder(),
                new ChordSimplifier(catalog),
                new FileDialogService(),
                _separator,
                new AudioPlayer(),
                audio,
                audioConnected,
                sampleRate,
                tempo);

            DataContext = _viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _shownPixelsPerSecond = _viewModel.PixelsPerSecond;
        }
        catch (Exception ex)
        {
            // The app and its other pages still start if the ONNX model/runtime is missing.
            InitErrorText.Text = "コード検出を初期化できませんでした。NuGetの復元と Models/nmp.onnx を確認してください。\n" + ex.Message;
            InitErrorText.Visibility = Visibility.Visible;
        }
    }

    public void NotifyAudioChanged() => _viewModel?.NotifySharedAudioChanged();

    private void OnAudioDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DataContext is ChordDetectionViewModel { IsIdle: true } &&
                    DroppedFile(e) is { } path && ChordDetectionViewModel.IsSupportedAudioFile(path)
                    ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnAudioDrop(object sender, DragEventArgs e)
    {
        if (DataContext is ChordDetectionViewModel vm && DroppedFile(e) is { } path)
            await vm.OpenDroppedFileAsync(path);
    }

    private static string? DroppedFile(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files ? files[0] : null;

    private void OnChordClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChordRowViewModel row } && _viewModel is { } vm)
            vm.PlayFrom(row.StartSeconds);
    }

    private void OnBarClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BarRowViewModel bar } && _viewModel is { } vm)
            vm.PlayFrom(Math.Max(0, bar.StartSeconds));
    }

    // ---------- loop range dragged on the ruler ----------

    // Pointer this close (px) to an end of the loop drags that end instead of starting a new range.
    private const double LoopEdgeGrip = 8;

    // Less movement than this (px) is a click: the playback moves there.
    private const double LoopClickSlop = 4;

    private bool _loopDragging;
    private double _loopAnchorSeconds;   // the end that stays put while dragging
    private double _loopPressX;
    private bool _loopMoved;

    private void OnLoopRulerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is not { HasPlayback: true } vm) return;
        double x = e.GetPosition(LoopRuler).X;
        double pps = vm.PixelsPerSecond;
        _loopPressX = x;
        _loopMoved = false;
        _loopAnchorSeconds = vm.SnapLoopTime(x / pps);
        if (vm.LoopRange is { } range)
        {
            // Grabbing an end keeps the other end as the anchor.
            if (Math.Abs(x - range.Start * pps) <= LoopEdgeGrip) { _loopAnchorSeconds = range.End; _loopMoved = true; }
            else if (Math.Abs(x - range.End * pps) <= LoopEdgeGrip) { _loopAnchorSeconds = range.Start; _loopMoved = true; }
        }
        _loopDragging = LoopRuler.CaptureMouse();
        e.Handled = true;
    }

    private void OnLoopRulerMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel is not { } vm) return;
        double x = e.GetPosition(LoopRuler).X;
        if (!_loopDragging)
        {
            LoopRuler.Cursor = vm.LoopRange is { } range &&
                (Math.Abs(x - range.Start * vm.PixelsPerSecond) <= LoopEdgeGrip || Math.Abs(x - range.End * vm.PixelsPerSecond) <= LoopEdgeGrip)
                ? Cursors.SizeWE : Cursors.IBeam;
            return;
        }
        if (Math.Abs(x - _loopPressX) >= LoopClickSlop) _loopMoved = true;
        if (_loopMoved) vm.PreviewLoop(_loopAnchorSeconds, vm.SnapLoopTime(x / vm.PixelsPerSecond));
    }

    private void OnLoopRulerMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_loopDragging) return;
        _loopDragging = false;
        LoopRuler.ReleaseMouseCapture();
        if (_viewModel is not { } vm) return;
        double seconds = e.GetPosition(LoopRuler).X / vm.PixelsPerSecond;
        if (_loopMoved) vm.SetLoop(_loopAnchorSeconds, vm.SnapLoopTime(seconds));
        else vm.PlayFrom(Math.Max(0, seconds)); // a click (not snapped)
        e.Handled = true;
    }

    private void OnLoopRulerLostCapture(object sender, MouseEventArgs e)
    {
        // Capture taken away mid-drag (e.g. another window): keep what was dragged so far.
        if (!_loopDragging) return;
        _loopDragging = false;
        if (_loopMoved && _viewModel?.LoopRange is { } range) _viewModel.SetLoop(range.Start, range.End);
    }

    // ---------- scrolling the timeline (wheel / drag) ----------

    // One wheel notch (Delta 120) moves about an eighth of the view.
    private const double WheelScreenFraction = 1.0 / 8;

    // After the user scrolled, the playback follow-up waits this long before pulling the view back.
    private static readonly TimeSpan ManualScrollPause = TimeSpan.FromSeconds(3);

    private DateTime _manualScrollUntil;
    private bool _panPressed;   // left button down on the lanes (may still become a click)
    private bool _panning;      // moved far enough: the view follows the pointer
    private double _panPressX;
    private double _panStartOffset;

    private void OnTimelineMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return; // not assigned (zoom is タイムラインの幅)
        ScrollTimelineBy(-WheelPixels(e.Delta));
        e.Handled = true;
    }

    /// <summary>Pixels for a wheel delta (120 = one notch); small touchpad deltas move a little, smoothly.</summary>
    private double WheelPixels(int delta) => TimelineScroll.ViewportWidth * WheelScreenFraction * delta / 120;

    /// <summary>Scrolls by the user (wheel / touchpad): the playback follow-up then waits for a moment.</summary>
    private void ScrollTimelineBy(double pixels)
    {
        TimelineScroll.ScrollToHorizontalOffset(TimelineScroll.HorizontalOffset + pixels);
        _manualScrollUntil = DateTime.UtcNow + ManualScrollPause;
    }

    // ---------- touchpad: two-finger left / right swipes ----------
    // They arrive as WM_MOUSEHWHEEL, which WPF has no event for (as in DrumEditorView.Trackpad.cs).

    private const int WM_MOUSEHWHEEL = 0x020E;
    private HwndSource? _trackpadSource;

    private void AttachTrackpad()
    {
        if (_trackpadSource is not null || Window.GetWindow(this) is not { } window) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        _trackpadSource = HwndSource.FromHwnd(handle);
        _trackpadSource?.AddHook(OnTrackpadMessage);
    }

    private void DetachTrackpad()
    {
        _trackpadSource?.RemoveHook(OnTrackpadMessage);
        _trackpadSource = null;
    }

    private IntPtr OnTrackpadMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WM_MOUSEHWHEEL || !TimelineScroll.IsVisible) return IntPtr.Zero;
        long location = lParam.ToInt64();
        var screen = new Point(unchecked((short)(location & 0xffff)), unchecked((short)((location >> 16) & 0xffff)));
        var p = TimelineScroll.PointFromScreen(screen);
        // Swipes elsewhere (other pages, the drum editor) are left to their own handlers.
        if (p.X < 0 || p.Y < 0 || p.X >= TimelineScroll.ActualWidth || p.Y >= TimelineScroll.ActualHeight) return IntPtr.Zero;
        int delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xffff)); // positive: to the right
        ScrollTimelineBy(WheelPixels(delta));
        handled = true;
        return IntPtr.Zero;
    }

    private void OnTimelinePanDown(object sender, MouseButtonEventArgs e)
    {
        // The ruler sets the loop and the scroll bar scrolls by itself.
        if (IsInside<ScrollBar>(e.OriginalSource) || IsUnder(e.OriginalSource, LoopRuler)) return;
        _panPressed = true;
        _panning = false;
        _panPressX = e.GetPosition(TimelineScroll).X;
        _panStartOffset = TimelineScroll.HorizontalOffset;
        // Not handled: without moving, the press stays a click (a chord plays from there).
    }

    private void OnTimelinePanMove(object sender, MouseEventArgs e)
    {
        if (!_panPressed) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPan(); return; }
        double dx = e.GetPosition(TimelineScroll).X - _panPressX;
        if (!_panning)
        {
            if (Math.Abs(dx) < LoopClickSlop) return;
            _panning = TimelineScroll.CaptureMouse();
            if (!_panning) return;
            Mouse.OverrideCursor = Cursors.SizeWE;
        }
        TimelineScroll.ScrollToHorizontalOffset(_panStartOffset - dx);
        _manualScrollUntil = DateTime.UtcNow + ManualScrollPause;
        e.Handled = true;
    }

    private void OnTimelinePanUp(object sender, MouseButtonEventArgs e)
    {
        if (!_panPressed) return;
        // After a drag the release must not count as a click on the chord under the pointer.
        if (_panning) e.Handled = true;
        EndPan();
    }

    private void OnTimelinePanLostCapture(object sender, MouseEventArgs e)
    {
        if (_panning) EndPan();
    }

    private void EndPan()
    {
        bool wasPanning = _panning;
        _panPressed = false;
        _panning = false;
        if (!wasPanning) return;
        Mouse.OverrideCursor = null;
        if (TimelineScroll.IsMouseCaptured) TimelineScroll.ReleaseMouseCapture();
    }

    private static bool IsInside<T>(object source) where T : DependencyObject
    {
        for (var node = source as DependencyObject; node is not null; node = Parent(node))
            if (node is T) return true;
        return false;
    }

    private static bool IsUnder(object source, DependencyObject ancestor)
    {
        for (var node = source as DependencyObject; node is not null; node = Parent(node))
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    // Text elements (Run …) are not visuals: go up the logical tree for them.
    private static DependencyObject? Parent(DependencyObject node) =>
        node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ChordDetectionViewModel vm) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnViewModelPropertyChanged(sender, e)));
            return;
        }
        if (e.PropertyName == nameof(ChordDetectionViewModel.PlaybackLeft))
        {
            FollowPlayback(vm);
            return;
        }
        if (e.PropertyName == nameof(ChordDetectionViewModel.PixelsPerSecond))
        {
            KeepViewCentre(vm.PixelsPerSecond);
            return;
        }
        if (e.PropertyName != nameof(ChordDetectionViewModel.PlayheadLeft)) return;
        if (vm.PlayheadLeft == 0)
        {
            TimelineScroll.ScrollToHorizontalOffset(0);
            return;
        }
        double target = vm.PlayheadLeft - TimelineScroll.ViewportWidth * .8;
        if (target > TimelineScroll.HorizontalOffset)
            TimelineScroll.ScrollToHorizontalOffset(target);
    }

    private double _shownPixelsPerSecond;

    /// <summary>After a change of タイムラインの幅, keeps the second that was in the middle of the view in the middle.</summary>
    private void KeepViewCentre(double pixelsPerSecond)
    {
        double previous = _shownPixelsPerSecond;
        _shownPixelsPerSecond = pixelsPerSecond;
        if (previous <= 0) return;
        double width = TimelineScroll.ViewportWidth;
        double centreSeconds = (TimelineScroll.HorizontalOffset + width / 2) / previous;
        // After the layout of the new width (and after the playhead follow-up of the same refresh).
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, centreSeconds * pixelsPerSecond - width / 2))));
    }

    private void FollowPlayback(ChordDetectionViewModel vm)
    {
        if (!vm.IsPlaying || _panning || DateTime.UtcNow < _manualScrollUntil) return; // the user is looking elsewhere
        double left = TimelineScroll.HorizontalOffset, width = TimelineScroll.ViewportWidth;
        if (vm.PlaybackLeft < left || vm.PlaybackLeft > left + width * .9)
            TimelineScroll.ScrollToHorizontalOffset(vm.PlaybackLeft - width * .1);
    }

    public void Dispose()
    {
        DetachTrackpad();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }
        _separator?.Dispose();
        _separator = null;
        _model?.Dispose();
        _model = null;
    }
}
}

namespace ChordDetection.App.Views
{
    /// <summary>true → Collapsed, false → Visible. Retained for original styles.</summary>
    public sealed class InvertedBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility.Collapsed;
    }
}
