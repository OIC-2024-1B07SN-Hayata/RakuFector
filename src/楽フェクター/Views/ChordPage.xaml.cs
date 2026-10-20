using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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

    public ChordPage(CommonAudioInput audio, Func<bool> audioConnected, Func<int> sampleRate)
    {
        InitializeComponent();

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
                sampleRate);

            DataContext = _viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
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

    private void FollowPlayback(ChordDetectionViewModel vm)
    {
        if (!vm.IsPlaying) return;
        double left = TimelineScroll.HorizontalOffset, width = TimelineScroll.ViewportWidth;
        if (vm.PlaybackLeft < left || vm.PlaybackLeft > left + width * .9)
            TimelineScroll.ScrollToHorizontalOffset(vm.PlaybackLeft - width * .1);
    }

    public void Dispose()
    {
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
