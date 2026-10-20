using System.Windows;
using System.Windows.Media;
namespace FXForge.Controls;
public sealed class NavGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty=DependencyProperty.Register(nameof(Kind),typeof(string),typeof(NavGlyph),new FrameworkPropertyMetadata("Sliders",FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty=DependencyProperty.Register(nameof(Stroke),typeof(Brush),typeof(NavGlyph),new FrameworkPropertyMetadata(Brushes.SlateGray,FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get=>(string)GetValue(KindProperty);set=>SetValue(KindProperty,value); }
    public Brush Stroke { get=>(Brush)GetValue(StrokeProperty);set=>SetValue(StrokeProperty,value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);dc.PushTransform(new ScaleTransform(ActualWidth/36,ActualHeight/36));var pen=new Pen(Stroke,2) { StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round };
        if(Kind=="Sliders")
        { for(int i=0;i<3;i++) { double x=7+i*11,y=i switch {0=>14,1=>23,_=>10};dc.DrawLine(pen,new(x,3),new(x,33));dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(23,25,28)),pen,new(x,y),3,3); } }
        else if(Kind=="Gauge")
        { var path=new StreamGeometry();using(var c=path.Open()) { c.BeginFigure(new(3,27),false,false);c.ArcTo(new(33,27),new(15,15),0,false,SweepDirection.Clockwise,true,false); }dc.DrawGeometry(null,pen,path);dc.DrawLine(pen,new(18,25),new(24,15));dc.DrawEllipse(Stroke,null,new(18,25),2,2); }
        else if(Kind=="Grid")
        { for(int y=0;y<2;y++)for(int x=0;x<2;x++) { dc.DrawRoundedRectangle(null,pen,new(3+x*17,3+y*17,13,13),3,3);dc.DrawEllipse(Stroke,null,new(9.5+x*17,9.5+y*17),1.5,1.5); } }
        else if(Kind=="List")
        { for(int i=0;i<3;i++) { dc.DrawEllipse(Stroke,null,new(5,7+i*11),2,2);dc.DrawLine(pen,new(13,7+i*11),new(32,7+i*11)); } }
        else
        { dc.DrawRoundedRectangle(null,pen,new(8,2,20,32),4,4);dc.DrawEllipse(null,pen,new(18,12),4,4);dc.DrawEllipse(null,pen,new(18,24),4,4); }
        dc.Pop();
    }
}
