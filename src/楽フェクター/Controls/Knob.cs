using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace FXForge.Controls;
public sealed class Knob : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(double),typeof(Knob),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender|FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public static readonly DependencyProperty MinimumProperty=DependencyProperty.Register(nameof(Minimum),typeof(double),typeof(Knob),new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty=DependencyProperty.Register(nameof(Maximum),typeof(double),typeof(Knob),new FrameworkPropertyMetadata(100.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty=DependencyProperty.Register(nameof(Accent),typeof(Brush),typeof(Knob),new FrameworkPropertyMetadata(Brushes.Goldenrod,FrameworkPropertyMetadataOptions.AffectsRender));
    public double Value { get=>(double)GetValue(ValueProperty);set=>SetCurrentValue(ValueProperty,value); }
    public double Minimum { get=>(double)GetValue(MinimumProperty);set=>SetValue(MinimumProperty,value); }
    public double Maximum { get=>(double)GetValue(MaximumProperty);set=>SetValue(MaximumProperty,value); }
    public Brush Accent { get=>(Brush)GetValue(AccentProperty);set=>SetValue(AccentProperty,value); }
    private Point _start;private double _initial;
    public Knob() { Cursor=Cursors.SizeNS;Focusable=true; }
    private void Change(double value)=>Value=Math.Clamp(value,Minimum,Maximum);
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus();_start=e.GetPosition(this);_initial=Value;CaptureMouse();e.Handled=true; }
    protected override void OnMouseMove(MouseEventArgs e) { if(IsMouseCaptured) Change(_initial+(_start.Y-e.GetPosition(this).Y)*(Maximum-Minimum)/180); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { ReleaseMouseCapture();e.Handled=true; }
    protected override void OnMouseWheel(MouseWheelEventArgs e) { Change(Value+Math.Sign(e.Delta)*(Maximum-Minimum)/100);e.Handled=true; }
    protected override void OnKeyDown(KeyEventArgs e)
    { if(e.Key is Key.Up or Key.Right) Change(Value+(Maximum-Minimum)/100);else if(e.Key is Key.Down or Key.Left) Change(Value-(Maximum-Minimum)/100);else return;e.Handled=true; }
    private static Geometry Arc(Point center,double radius,double sweep)
    {
        double angle=135*Math.PI/180,end=(135+sweep)*Math.PI/180;
        var path=new StreamGeometry();using(var ctx=path.Open())
        { ctx.BeginFigure(new(center.X+radius*Math.Cos(angle),center.Y+radius*Math.Sin(angle)),false,false);
          ctx.ArcTo(new(center.X+radius*Math.Cos(end),center.Y+radius*Math.Sin(end)),new(radius,radius),0,sweep>180,SweepDirection.Clockwise,true,false); }
        path.Freeze();return path;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.DrawRectangle(Brushes.Transparent,null,new Rect(RenderSize));
        double diameter=Math.Min(ActualWidth,ActualHeight)-20,r=Math.Max(1,diameter/2);var center=new Point(ActualWidth/2,ActualHeight/2);
        var basePen=new Pen(new SolidColorBrush(Color.FromRgb(62,69,77)),12) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round };
        var accentPen=new Pen(Accent,12) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round };
        dc.DrawGeometry(null,basePen,Arc(center,r,270));
        double ratio=Math.Clamp((Value-Minimum)/Math.Max(1e-6,Maximum-Minimum),0,1);
        if(ratio>.001) dc.DrawGeometry(null,accentPen,Arc(center,r,270*ratio));
        var text=new FormattedText(Value.ToString("0",CultureInfo.CurrentCulture),CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),Math.Clamp(diameter*.29,24,58),Brushes.White,VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text,new Point(center.X-text.Width/2,center.Y-text.Height/2));
    }
}
