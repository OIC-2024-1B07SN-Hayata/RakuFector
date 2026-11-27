using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FXForge.Models;

namespace FXForge.Views;

// TonePage.xaml.cs と同じクラスへ機能を追加する別ファイルです。
public partial class TonePage
{
    private bool _xyMode;
    private bool _xyAttached;
    private bool _xyQueued;
    private EffectItem? _xyEffect;
    private readonly HashSet<EffectParameter> _xyParameters = new();

    // 今後の対応機種は、この表へ追加します。
    private static readonly Dictionary<string, (string X, string Y)> XyMappings = new()
    {
        // （横軸、縦軸）。左右どちらのDRIVEスロットでも共通です。
        ["RAT"] = ("Volume", "Distortion"),
        ["SD-1"] = ("Level", "Drive"),
        ["TS9"] = ("Level", "Drive"),
        ["BD-2"] = ("Level", "Gain"),
        ["DS-1"] = ("Level", "Distortion")
    };

    private void XyPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_xyAttached) return;
        _xyAttached = true;
        PropertyChanged += XyPagePropertyChanged;
        XyObserveSelection();
        XyRefresh();
    }

    private void XyPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _xyAttached = false;
        PropertyChanged -= XyPagePropertyChanged;
        XyDetachEffect();
        XyCanvas.ReleaseMouseCapture();
    }

    private void XyPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SelectedEffect))
        {
            XyCanvas.ReleaseMouseCapture();
            XyObserveSelection();
            XyRequestRefresh();
        }
    }

    private void XyDetachEffect()
    {
        if (_xyEffect is not null)
        {
            _xyEffect.PropertyChanged -= XyEffectChanged;
            _xyEffect.Parameters.CollectionChanged -= XyCollectionChanged;
        }
        foreach (var parameter in _xyParameters)
            parameter.PropertyChanged -= XyParameterChanged;
        _xyParameters.Clear();
        _xyEffect = null;
    }

    private void XyObserveSelection()
    {
        XyDetachEffect();
        _xyEffect = SelectedEffect;
        if (_xyEffect is null) return;
        _xyEffect.PropertyChanged += XyEffectChanged;
        _xyEffect.Parameters.CollectionChanged += XyCollectionChanged;
        XyObserveParameters();
    }

    private void XyObserveParameters()
    {
        foreach (var parameter in _xyParameters)
            parameter.PropertyChanged -= XyParameterChanged;
        _xyParameters.Clear();
        if (_xyEffect is null) return;
        foreach (var parameter in _xyEffect.Parameters)
        {
            _xyParameters.Add(parameter);
            parameter.PropertyChanged += XyParameterChanged;
        }
    }

    private void XyCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        XyObserveParameters();
        XyRequestRefresh();
    }

    private void XyEffectChanged(object? sender, PropertyChangedEventArgs e)
        => XyRequestRefresh();

    private void XyParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EffectParameter.Value) or nameof(EffectParameter.IsEditable))
            XyRequestRefresh();
    }

    // 設定更新をまとめ、プリセット変更途中の一時的な空配列で描かないようにする。
    private void XyRequestRefresh()
    {
        if (!_xyAttached || _xyQueued) return;
        _xyQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _xyQueued = false;
            if (_xyAttached) XyRefresh();
        }));
    }

    private (EffectParameter? X, EffectParameter? Y) XyGetParameters()
    {
        if (SelectedEffect is null || !XyMappings.TryGetValue(SelectedEffect.ShortName, out var map))
            return (null, null);
        return (SelectedEffect.Parameters.FirstOrDefault(p => p.Name == map.X),
                SelectedEffect.Parameters.FirstOrDefault(p => p.Name == map.Y));
    }

    private void XyRefresh()
    {
        var (x, y) = XyGetParameters();
        // 連動する2個：縦軸、横軸の順。個別操作は別の列へ表示。
        XySetKnobItems(XyKnobs, new[] { y, x }.OfType<EffectParameter>());
        XySetKnobItems(XyIndependentKnobs,
            (SelectedEffect?.Parameters.AsEnumerable() ?? Enumerable.Empty<EffectParameter>())
            .Where(p => !ReferenceEquals(p, x) && !ReferenceEquals(p, y)));
        bool available = x is not null && y is not null;
        if (!available) _xyMode = false;
        if (!_xyMode && XyCanvas.IsMouseCaptured) XyCanvas.ReleaseMouseCapture();
        XyModeButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        XyModeButton.Content = _xyMode ? "ノブ表示に戻す" : "XY操作";
        NormalKnobArea.Visibility = _xyMode ? Visibility.Collapsed : Visibility.Visible;
        XyArea.Visibility = _xyMode ? Visibility.Visible : Visibility.Collapsed;
        // 操作説明は通常ノブ画面だけに表示する。
        XyHelpText.Visibility = _xyMode ? Visibility.Collapsed : Visibility.Visible;
        XyHelpText.Text = _xyMode
            ? "黄色い点をドラッグして調整。右のノブも連動します。グラフ選択後は矢印キーでも調整できます。"
            : "ノブ：上下ドラッグ・マウスホイール・矢印キーで調整";
        if (available)
            System.Windows.Automation.AutomationProperties.SetName(XyCanvas,
                $"XY操作。横軸は{x!.Name}、縦軸は{y!.Name}");
        XyArrangeLayout();
        XyDraw();
    }

    private void XyModeButton_Click(object sender, RoutedEventArgs e)
    {
        _xyMode = !_xyMode;
        XyRefresh();
    }

    private static void XySetKnobItems(ItemsControl control, IEnumerable<EffectParameter> items)
    {
        var values = items.ToArray();
        if (control.ItemsSource is not IEnumerable<EffectParameter> current ||
            !current.SequenceEqual(values))
            control.ItemsSource = values;
    }

    private void XyEditor_SizeChanged(object sender, SizeChangedEventArgs e)
        => XyArrangeLayout();

    private void XyArea_SizeChanged(object sender, SizeChangedEventArgs e)
        => XyArrangeLayout();

    private void ToneScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        => XyRequestRefresh();

    private void XyArrangeLayout()
    {
        if (XyEditor is null || XyArea is null) return;
        bool wide = _xyMode && XyEditor.ActualWidth >= 1000;
        XyEditor.ColumnDefinitions[0].Width = new GridLength(wide ? 240 : 0);
        XyEditor.ColumnDefinitions[1].Width = new GridLength(wide ? 16 : 0);
        Grid.SetColumnSpan(XyEditorHeader, 3);
        Grid.SetColumnSpan(XyTypeSelector, wide ? 1 : 3);
        XyEditorDivider.Visibility = _xyMode ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(XyHeaderActions, 0);
        Grid.SetColumn(XyHeaderActions, 3);
        Grid.SetColumnSpan(XyHeaderActions, 1);
        XyHeaderActions.HorizontalAlignment = HorizontalAlignment.Right;
        XyHeaderActions.Margin = new Thickness(0);

        // 横幅があるときは見出しと同じ高さからグラフを置く。
        Grid.SetColumn(XyArea, wide ? 2 : 0);
        Grid.SetColumnSpan(XyArea, wide ? 1 : 3);
        Grid.SetRow(XyArea, wide ? 0 : 3);
        Grid.SetRowSpan(XyArea, wide ? 6 : 1);
        XyArea.Margin = new Thickness(0, 8, 0, 0);
        bool narrow = XyArea.ActualWidth < 600;

        double viewport = ToneScrollViewer.ViewportHeight;
        if (viewport <= 0) viewport = ToneScrollViewer.ActualHeight;
        // 見出しとグラフが横並びなので、広い画面では見出しの高さを引かない。
        double reserved = ToneSignalChain.ActualHeight + 20 + 44 + 44 + 16;
        if (!wide) reserved += XyEditorHeader.ActualHeight + 16 + XyTypeSelector.ActualHeight;
        if (narrow) reserved += Math.Max(XyKnobs.ActualHeight, XyIndependentKnobs.ActualHeight);
        double heightLimit = viewport > 0 ? Math.Max(100, viewport - reserved - 86) : 420;
        double widthLimit = narrow ? XyArea.ActualWidth - 90 : XyArea.ActualWidth - 344 - 150;
        double side = Math.Max(80, Math.Min(460, Math.Min(heightLimit, widthLimit)));

        var columns = XyArea.ColumnDefinitions;
        // グラフ列を固定幅にし、連動ノブが右端へ離れすぎるのを防ぐ。
        columns[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(side + 150);
        columns[1].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(160);
        columns[2].Width = new GridLength(narrow ? 0 : 24);
        columns[3].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumnSpan(XyCanvas, narrow ? 4 : 1);
        Grid.SetRow(XyKnobs, narrow ? 1 : 0);
        Grid.SetColumn(XyKnobs, narrow ? 0 : 1);
        Grid.SetRow(XyIndependentKnobs, narrow ? 1 : 0);
        Grid.SetColumn(XyIndependentKnobs, narrow ? 1 : 3);
        XyKnobs.VerticalAlignment = VerticalAlignment.Center;
        XyIndependentKnobs.VerticalAlignment = VerticalAlignment.Center;
        double wantedHeight = side + 86;
        if (Math.Abs(XyCanvas.Height - wantedHeight) > 0.5)
            XyCanvas.Height = wantedHeight;
        XyDraw();
    }

    private void XyCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => XyDraw();

    private Rect XyBounds()
    {
        double width = Math.Max(0, XyCanvas.ActualWidth - 70);
        double height = Math.Max(0, XyCanvas.ActualHeight - 86);
        double side = Math.Min(width, height);
        return new Rect(44 + (width - side) / 2, 30, side, side);
    }

    private static double XyRatio(EffectParameter p) => p.Maximum > p.Minimum
        ? Math.Clamp((p.Value - p.Minimum) / (p.Maximum - p.Minimum), 0, 1) : 0;

    private void XyLine(double x1, double y1, double x2, double y2, Brush color, bool dashed = false)
    {
        var line = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = color,
            StrokeThickness = 1,
            IsHitTestVisible = false
        };
        if (dashed) line.StrokeDashArray = new DoubleCollection { 4, 4 };
        XyCanvas.Children.Add(line);
    }

    private void XyText(string text, double x, double y, bool centered = false, bool right = false, double fontSize = 11)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(164, 178, 191)),
            IsHitTestVisible = false
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(label, x - (centered ? label.DesiredSize.Width / 2 : right ? label.DesiredSize.Width : 0));
        Canvas.SetTop(label, y);
        XyCanvas.Children.Add(label);
    }

    private void XyDraw()
    {
        XyCanvas.Children.Clear();
        if (!_xyMode) return;
        var (x, y) = XyGetParameters();
        var b = XyBounds();
        if (x is null || y is null || b.Width < 1 || b.Height < 1) return;
        var grid = new SolidColorBrush(Color.FromRgb(41, 53, 64));
        var axis = new SolidColorBrush(Color.FromRgb(117, 132, 148));
        for (int i = 0; i <= 4; i++)
        {
            double t = i / 4.0;
            double px = b.Left + b.Width * t;
            double py = b.Bottom - b.Height * t;
            XyLine(px, b.Top, px, b.Bottom, grid);
            XyLine(b.Left, py, b.Right, py, grid);
            XyText($"{x.Minimum + (x.Maximum - x.Minimum) * t:0.##}", px, b.Bottom + 8, centered: true);
            XyText($"{y.Minimum + (y.Maximum - y.Minimum) * t:0.##}", b.Left - 8, py - 8, right: true);
        }
        XyLine(b.Left, b.Top, b.Left, b.Bottom, axis);
        XyLine(b.Left, b.Bottom, b.Right, b.Bottom, axis);
        // 縦軸：Driveなど
        XyText(
            y.Name + " ↑",
            b.Left,
            4,
            fontSize: 16);

        // 横軸：Levelなど
        XyText(
            x.Name + " →",
            b.Left + b.Width / 2,
            b.Bottom + 32,
            centered: true,
            fontSize: 16);
        double cx = b.Left + b.Width * XyRatio(x);
        double cy = b.Bottom - b.Height * XyRatio(y);
        XyLine(cx, b.Top, cx, b.Bottom, axis, true);
        XyLine(b.Left, cy, b.Right, cy, axis, true);
        var dot = new Ellipse
        {
            Width = 16,
            Height = 16,
            Fill = Brushes.Gold,
            Stroke = Brushes.PaleGoldenrod,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(dot, cx - 8); Canvas.SetTop(dot, cy - 8);
        XyCanvas.Children.Add(dot);
    }

    private void XySetFromMouse(MouseEventArgs e)
    {
        var (x, y) = XyGetParameters();
        var b = XyBounds();
        if (x is null || y is null || b.Width < 1 || b.Height < 1) return;
        var p = e.GetPosition(XyCanvas);
        // Stepに応じた丸めは既存のEffectParameter.Valueへ任せる。
        if (x.IsEditable) x.Value = x.Minimum + (x.Maximum - x.Minimum) * Math.Clamp((p.X - b.Left) / b.Width, 0, 1);
        if (y.IsEditable) y.Value = y.Minimum + (y.Maximum - y.Minimum) * Math.Clamp((b.Bottom - p.Y) / b.Height, 0, 1);
        XyRequestRefresh();
    }

    private void XyCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!XyBounds().Contains(e.GetPosition(XyCanvas))) return;
        XyCanvas.Focus(); XyCanvas.CaptureMouse();
        XySetFromMouse(e); e.Handled = true;
    }
    private void XyCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!XyCanvas.IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed) { XyCanvas.ReleaseMouseCapture(); return; }
        XySetFromMouse(e); e.Handled = true;
    }
    private void XyCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!XyCanvas.IsMouseCaptured) return;
        XySetFromMouse(e); XyCanvas.ReleaseMouseCapture(); e.Handled = true;
    }
    private void XyCanvas_KeyDown(object sender, KeyEventArgs e)
    {
        var (x, y) = XyGetParameters();
        var parameter = e.Key is Key.Left or Key.Right ? x : e.Key is Key.Up or Key.Down ? y : null;
        if (parameter is null || !parameter.IsEditable) return;
        double step = parameter.Step > 0 ? parameter.Step : (parameter.Maximum - parameter.Minimum) / 100;
        parameter.Value += (e.Key is Key.Left or Key.Down ? -1 : 1) * step;
        XyRequestRefresh(); e.Handled = true;
    }
}
