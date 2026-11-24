using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace FXForge.Controls;

public sealed class Knob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(Knob),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(
            nameof(Minimum),
            typeof(double),
            typeof(Knob),
            new FrameworkPropertyMetadata(
                0.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(double),
            typeof(Knob),
            new FrameworkPropertyMetadata(
                100.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.Register(
            nameof(Accent),
            typeof(Brush),
            typeof(Knob),
            new FrameworkPropertyMetadata(
                Brushes.Goldenrod,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StepProperty =
        DependencyProperty.Register(
            nameof(Step),
            typeof(double),
            typeof(Knob),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty DisplayTextProperty =
        DependencyProperty.Register(
            nameof(DisplayText),
            typeof(string),
            typeof(Knob),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetCurrentValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush Accent
    {
        get => (Brush)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string? DisplayText
    {
        get => (string?)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value);
    }

    private double Increment =>
        Step > 0 ? Step : (Maximum - Minimum) / 100;

    private Point _start;
    private double _initial;

    public Knob()
    {
        Cursor = Cursors.SizeNS;
        Focusable = true;
    }

    private void Change(double value)
    {
        if (Step > 0)
        {
            value =
                Math.Round(value / Step, MidpointRounding.AwayFromZero) *
                Step;
        }

        Value = Math.Clamp(value, Minimum, Maximum);
    }

    protected override void OnMouseLeftButtonDown(
        MouseButtonEventArgs e)
    {
        Focus();

        _start = e.GetPosition(this);
        _initial = Value;

        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured)
        {
            Change(
                _initial +
                (_start.Y - e.GetPosition(this).Y) *
                (Maximum - Minimum) / 180);
        }
    }

    protected override void OnMouseLeftButtonUp(
        MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Change(Value + Math.Sign(e.Delta) * Increment);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Right)
            Change(Value + Increment);
        else if (e.Key is Key.Down or Key.Left)
            Change(Value - Increment);
        else
            return;

        e.Handled = true;
    }

    private static Geometry Arc(
        Point center,
        double radius,
        double sweep)
    {
        double angle = 135 * Math.PI / 180;
        double end = (135 + sweep) * Math.PI / 180;

        var path = new StreamGeometry();

        using (var context = path.Open())
        {
            context.BeginFigure(
                new Point(
                    center.X + radius * Math.Cos(angle),
                    center.Y + radius * Math.Sin(angle)),
                false,
                false);

            context.ArcTo(
                new Point(
                    center.X + radius * Math.Cos(end),
                    center.Y + radius * Math.Sin(end)),
                new Size(radius, radius),
                0,
                sweep > 180,
                SweepDirection.Clockwise,
                true,
                false);
        }

        path.Freeze();
        return path;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        dc.DrawRectangle(
            Brushes.Transparent,
            null,
            new Rect(RenderSize));

        double diameter =
            Math.Min(ActualWidth, ActualHeight) - 20;

        double radius = Math.Max(1, diameter / 2);

        var center = new Point(
            ActualWidth / 2,
            ActualHeight / 2);

        var basePen = new Pen(
            new SolidColorBrush(Color.FromRgb(62, 69, 77)),
            12)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        var accentPen = new Pen(Accent, 12)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };

        dc.DrawGeometry(
            null,
            basePen,
            Arc(center, radius, 270));

        double ratio = Math.Clamp(
            (Value - Minimum) /
            Math.Max(1e-6, Maximum - Minimum),
            0,
            1);

        if (ratio > .001)
        {
            dc.DrawGeometry(
                null,
                accentPen,
                Arc(center, radius, 270 * ratio));
        }

        string label =
            DisplayText ??
            Value.ToString("0", CultureInfo.CurrentCulture);

        double fontSize =
            Math.Clamp(diameter * .29, 24, 58);

        if (DisplayText is not null)
        {
            fontSize = Math.Min(
                fontSize,
                diameter * .8 / Math.Max(1, label.Length));
        }

        var text = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            fontSize,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        dc.DrawText(
            text,
            new Point(
                center.X - text.Width / 2,
                center.Y - text.Height / 2));
    }
}