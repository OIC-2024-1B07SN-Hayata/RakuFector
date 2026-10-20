using System.Windows;
using System.Windows.Media;

namespace ChordDetection.App.Views;

/// <summary>Input level as a row of segments; segments up to <see cref="Level"/> (0..1) are lit.</summary>
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(int), typeof(LevelMeter),
        new FrameworkPropertyMetadata(22, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OffBrushProperty = DependencyProperty.Register(
        nameof(OffBrush), typeof(Brush), typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OnBrushProperty = DependencyProperty.Register(
        nameof(OnBrush), typeof(Brush), typeof(LevelMeter),
        new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public int Segments
    {
        get => (int)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public Brush OffBrush
    {
        get => (Brush)GetValue(OffBrushProperty);
        set => SetValue(OffBrushProperty, value);
    }

    public Brush OnBrush
    {
        get => (Brush)GetValue(OnBrushProperty);
        set => SetValue(OnBrushProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        const double gap = 4;
        int n = Math.Max(1, Segments);
        double width = (ActualWidth - gap * (n - 1)) / n;
        if (width <= 0) return;
        int lit = (int)Math.Round(Math.Clamp(Level, 0, 1) * n);
        for (int i = 0; i < n; i++)
            dc.DrawRoundedRectangle(i < lit ? OnBrush : OffBrush, null,
                new Rect(i * (width + gap), 0, width, ActualHeight), 1.5, 1.5);
    }
}
