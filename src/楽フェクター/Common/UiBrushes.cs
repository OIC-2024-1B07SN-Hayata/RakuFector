using System.Windows.Media;

namespace FXForge.Common;

internal static class UiBrushes
{
    internal static SolidColorBrush BrushFrom(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        return new SolidColorBrush(color);
    }
}
