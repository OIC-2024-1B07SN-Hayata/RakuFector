using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommonAudio;
namespace FXForge;
public sealed class AudioSettingsWindow : Window
{
    public AudioPreferences? Preferences { get; private set; }
    public AudioSettingsWindow(AudioPreferences? current)
    {
        Title = "共通音声入出力の設定";
        Width = 520; Height = 650; MaxHeight = SystemParameters.WorkArea.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(16, 21, 27));
        Foreground = Brushes.White;
        var root = new Grid { Margin = new Thickness(24, 16, 24, 16) };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var panel = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        string[] drivers = CommonAudioInput.GetDriverNames();
        var driver = new ComboBox { ItemsSource = drivers };
        TextBox Number(int value) => new()
        {
            Text = value.ToString(CultureInfo.InvariantCulture), Padding = new Thickness(12, 8, 12, 8),
            Foreground = Brushes.Black, Background = Brushes.White
        };
        var input = Number((current?.Input ?? 0) + 1);
        var output = Number((current?.Output ?? 0) + 1);
        var rate = new ComboBox { ItemsSource = new[] { 44100, 48000, 96000 }, SelectedItem = current?.Rate ?? 48000 };
        void Add(string label, Control control)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(control);
        }
        Add("入力用ASIOドライバー", driver);
        var asioPanel = new Button
        {
            Content = "ASIO設定を開く（Buffer Size）", Margin = new Thickness(0, 8, 0, 0)
        };
        asioPanel.Click += (_, _) =>
        {
            if (driver.SelectedItem is not string name) return;
            try
            {
                using var asio = new NAudio.Wave.AsioOut(name);
                asio.ShowControlPanel();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "ASIO設定"); }
        };
        panel.Children.Add(asioPanel);
        Add("ギターの入力番号（INPUT 1なら1、INPUT 2なら2）", input);
        Add("サンプリング周波数（Hz）", rate);
        var monitor = new CheckBox
        {
            Content = "ギター音を出力する", IsChecked = current?.MonitorOutput ?? true,
            Margin = new Thickness(0, 16, 0, 0), Foreground = Brushes.White
        };
        panel.Children.Add(monitor);
        var destination = new ComboBox
        {
            ItemsSource = new[] { "PCのスピーカー（Windows出力）", "UR22CなどのASIO機器から出力" },
            SelectedIndex = (current?.PcOutput ?? true) ? 0 : 1
        };
        Add("ギター音の出力先", destination);
        var pcDevice = new ComboBox();
        pcDevice.Items.Add(new PcAudioDevice("", "Windowsの既定の出力"));
        string? deviceError = null;
        try { foreach (var item in PcAudioDevice.GetDevices()) pcDevice.Items.Add(item); }
        catch (Exception ex) { deviceError = "PC出力機器を取得できません: " + ex.Message; }
        pcDevice.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(current?.PcDeviceId))
        {
            pcDevice.SelectedIndex = -1;
            foreach (PcAudioDevice item in pcDevice.Items)
                if (item.Id == current.PcDeviceId) { pcDevice.SelectedItem = item; break; }
            if (pcDevice.SelectedIndex < 0) deviceError = "前回のPC出力機器がありません。出力機器を選び直してください。";
        }
        Add("PCの出力機器（使うスピーカーを選択）", pcDevice);
        var pcLatency = new ComboBox
        {
            ItemsSource = new[] { 10, 20, 40, 60 }, SelectedItem = current?.PcLatencyMs ?? 10
        };
        if (pcLatency.SelectedIndex < 0) pcLatency.SelectedIndex = 0;
        Add("PC出力バッファ要求（ms）小さいほど遅延を抑える", pcLatency);
        Add("ASIO出力の先頭番号（UR22Cは通常1）", output);
        var hint = new TextBlock
        {
            Text = deviceError ?? "PC出力はまず10ms。音切れが出る場合は20msにします。\nASIO設定のBuffer Sizeは64～128 samplesから確認してください。\nUR22Cのヘッドフォンを使う場合はASIO出力を選びます。",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0),
            Foreground = deviceError is null ? Brushes.LightGray : Brushes.Orange
        };
        panel.Children.Add(hint);
        void RefreshDestination()
        {
            pcDevice.IsEnabled = destination.SelectedIndex == 0;
            pcLatency.IsEnabled = destination.SelectedIndex == 0;
            output.IsEnabled = destination.SelectedIndex == 1;
        }
        destination.SelectionChanged += (_, _) => RefreshDestination();
        RefreshDestination();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new Button { Content = "キャンセル", IsCancel = true };
        var apply = new Button { Content = "適用・接続", IsDefault = true, Margin = new Thickness(10, 0, 0, 0) };
        buttons.Children.Add(cancel); buttons.Children.Add(apply);
        Grid.SetRow(buttons, 1); root.Children.Add(buttons);
        apply.Click += (_, _) =>
        {
            bool pc = destination.SelectedIndex == 0;
            int outputNumber = 1;
            if (driver.SelectedItem is not string name ||
                !int.TryParse(input.Text, out int inputNumber) || inputNumber < 1 ||
                rate.SelectedItem is not int sampleRate ||
                (!pc && (!int.TryParse(output.Text, out outputNumber) || outputNumber < 1)))
            {
                MessageBox.Show(this, "ASIOドライバー、入力番号、周波数、ASIO出力番号を確認してください。", Title);
                return;
            }
            if (pc && monitor.IsChecked == true && pcDevice.SelectedItem is not PcAudioDevice)
            {
                MessageBox.Show(this, "PCの出力機器を選択してください。", Title); return;
            }
            var selectedDevice = pcDevice.SelectedItem as PcAudioDevice;
            Preferences = new AudioPreferences(name, inputNumber - 1, sampleRate,
                monitor.IsChecked == true, outputNumber - 1, pc,
                string.IsNullOrWhiteSpace(selectedDevice?.Id) ? null : selectedDevice.Id,
                string.IsNullOrWhiteSpace(selectedDevice?.Id) ? null : selectedDevice.Name,
                pcLatency.SelectedItem is int milliseconds ? milliseconds : 10);
            DialogResult = true;
        };
        if (current is not null) driver.SelectedItem = current.Driver;
        if (driver.SelectedIndex < 0 && drivers.Length > 0) driver.SelectedIndex = 0;
        if (drivers.Length == 0)
        {
            apply.IsEnabled = false;
            panel.Children.Insert(0, new TextBlock { Text = "ASIOドライバーが見つかりません。", Foreground = Brushes.Orange });
        }
    }
}
