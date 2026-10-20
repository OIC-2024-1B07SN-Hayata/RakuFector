using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DrumEditor.Views;

public partial class DrumEditorView
{
    HwndSource? trackpadSource;
    void InitializeTrackpad()
    {
        PreviewMouseWheel+=TrackpadWheel;
        Loaded += (_, _) =>
        {
            var window = Window.GetWindow(this);
            if (window is null || trackpadSource is not null) return;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == System.IntPtr.Zero) return;
            trackpadSource = HwndSource.FromHwnd(handle);
            trackpadSource?.AddHook(TrackpadMessage);
        };
        Unloaded += (_, _) => DisposeTrackpad();
    }
    void DisposeTrackpad()
    {
        trackpadSource?.RemoveHook(TrackpadMessage);
        trackpadSource = null;
    }
    static bool Inside(DependencyObject? element,DependencyObject ancestor)
    {
        while(element!=null){if(element==ancestor)return true;if(element is not Visual && element is not System.Windows.Media.Media3D.Visual3D)return false;element=VisualTreeHelper.GetParent(element);}return false;
    }
    ScrollViewer? TrackpadTarget(DependencyObject? hit)
    {
        if(Inside(hit,CardScroller))return CardScroller;
        if(Inside(hit,RollArea)&&!Inside(hit,Division))return Scroller;
        return null;
    }
    void ScrollTrackpad(ScrollViewer target,int delta,bool horizontal,bool nativeHorizontal=false)
    {
        // Preserve small wheel deltas from precision touchpads instead of rounding to lines.
        double distance=delta*48d/120*(nativeHorizontal?1:-1);
        if(horizontal)target.ScrollToHorizontalOffset(target.HorizontalOffset+distance);
        else target.ScrollToVerticalOffset(target.VerticalOffset+distance);
    }
    void TrackpadWheel(object sender,MouseWheelEventArgs e)
    {
        if(TrackpadTarget(e.OriginalSource as DependencyObject) is not {} target)return;
        if(target==Scroller&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){ZoomAt(e.Delta>0?1:-1,Mouse.GetPosition(Scroller).X);e.Handled=true;return;}
        ScrollTrackpad(target,e.Delta,target==CardScroller||(Keyboard.Modifiers&ModifierKeys.Shift)!=0);
        e.Handled=true;
    }
    System.IntPtr TrackpadMessage(System.IntPtr hwnd,int message,System.IntPtr wParam,System.IntPtr lParam,ref bool handled)
    {
        const int MouseHorizontalWheel=0x020E;
        if(message!=MouseHorizontalWheel)return System.IntPtr.Zero;
        long location=lParam.ToInt64();var position=PointFromScreen(new Point(unchecked((short)(location&0xffff)),unchecked((short)((location>>16)&0xffff))));
        if(TrackpadTarget(InputHitTest(position) as DependencyObject) is {} target)
        {
            int delta=unchecked((short)((wParam.ToInt64()>>16)&0xffff));
            ScrollTrackpad(target,delta,true,true);handled=true;
        }
        return System.IntPtr.Zero;
    }
}
