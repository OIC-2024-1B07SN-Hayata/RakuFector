using System.Windows;
using System.Windows.Media;
namespace DrumEditor.Controls;

public sealed class NavigationIcon : FrameworkElement
{
    public int Kind { get; set; }
    public Brush Stroke { get; set; } = Brushes.Gray;
    protected override void OnRender(DrawingContext dc)
    {
        var pen = new Pen(Stroke, 2);
        if (Kind == 0)
            for (int i = 0; i < 3; i++) { double x = 5 + i * 14; dc.DrawLine(pen, new(x, 2), new(x, 32)); dc.DrawRectangle(Stroke, null, new Rect(x - 3, 7 + i * 5, 6, 6)); }
        else if (Kind == 1) { dc.DrawEllipse(null, new Pen(Stroke, 3), new(19, 17), 15, 15); dc.DrawLine(pen, new(19, 20), new(29, 8)); }
        else if (Kind == 2)
            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) dc.DrawRectangle(null, pen, new Rect(5 + x * 17, 3 + y * 17, 11, 11));
        else if (Kind == 3)
            for (int i = 0; i < 3; i++) { dc.DrawRectangle(Stroke, null, new Rect(4, 4 + i * 10, 4, 4)); dc.DrawLine(pen, new(13, 6 + i * 10), new(36, 6 + i * 10)); }
        else { dc.DrawRectangle(null, pen, new Rect(7, 0, 25, 35)); dc.DrawEllipse(null, pen, new(19.5, 10), 6, 6); dc.DrawEllipse(null, pen, new(19.5, 25), 6, 6); }
    }
}
