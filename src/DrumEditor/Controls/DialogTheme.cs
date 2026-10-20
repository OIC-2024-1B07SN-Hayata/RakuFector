using System.Windows;
using System.Windows.Media;
namespace DrumEditor.Controls;
public static class DialogTheme
{
    public static void Apply(Window window)
    {
        window.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/DrumEditor;component/Controls/DialogStyles.xaml",UriKind.Relative)});
        window.Background=new SolidColorBrush(Color.FromRgb(23,26,31));window.Foreground=Brushes.White;
    }
}
