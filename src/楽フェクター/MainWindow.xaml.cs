using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FXForge.Models;
using System.Windows.Media;
using System.Windows.Threading;
using CommonAudio;
using FXForge.Integration;
using FXForge.Views;
using DrumEditor.Views;
namespace FXForge;
public partial class MainWindow : Window
{
    private readonly CommonAudioInput _audio = new();
    private readonly TonePage _tone = new();
    private readonly TunerPage _tuner = new();
    private readonly ChordPage _chord;
    private readonly DrumEditorView _drum = new();
    private readonly FootswitchPage _foot = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private AudioPreferences? _preferences;
    private bool _connected;
    private bool _checkingClose, _closeApproved;
    private long _receivedBlocks;
    public CommonAudioInput Audio => _audio;
    public TonePage ToneView => _tone;
    public TunerPage TunerView => _tuner;
    public ChordPage ChordView => _chord;
    public MainWindow()
    {
        InitializeComponent();
        _chord = new ChordPage(_audio, () => _connected, () => _preferences?.Rate ?? 0);
        // 表示倍率が高いPCでも、出力状態の表示が画面外へはみ出さないようにします。
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, workArea.Width);
        MinHeight = Math.Min(MinHeight, workArea.Height);
        Width = Math.Min(Width, workArea.Width);
        Height = Math.Min(Height, workArea.Height);
        _foot.ExecuteAction = ExecuteFootswitchAction;
        PreviewKeyDown += HandleFootswitchKeys;
        _drum.NavigationRequested += ShowPage;
        _drum.EditorTitleChanged += title => { if (ReferenceEquals(MainContent.Content, _drum)) Title = "楽フェクター — " + title; };
        ShowPage("Tone");
        _audio.AudioReceived += OnAudioReceived;
        MemberConnections.Configure(this, _audio);
        Loaded += OnLoaded; Closing += OnClosing;
        _timer.Tick += (_, _) => RefreshAudioStatus();
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _preferences = AudioPreferences.Load();
        if (_preferences is not null) ConnectAudio();
        else AudioStatusText.Text = "共通音声入力: 未接続（⚙から設定）";
        _chord.NotifyAudioChanged();
        _timer.Start();
    }
    private void OnAudioReceived(float[] samples, int sampleRate, int channels)
    {
        // 受信の有無だけを保持します。UIはタイマーから更新します。
        Interlocked.Increment(ref _receivedBlocks);
    }
    private void RefreshAudioStatus()
    {
        if (!_connected || _preferences is null) return;
        if (_audio.LastError is string error)
        {
            OutputTestButton.IsEnabled = false;
            AudioStatusText.Text = "共通音声入力: " + error; return;
        }
        string state = Interlocked.Read(ref _receivedBlocks) > 0 ? "受信中" : "入力待機中";
        string output = !_preferences.MonitorOutput ? "出力OFF（⚙でON）" :
            _preferences.PcOutput ? $"PC: {_audio.PcOutputName}" : $"OUT {_preferences.Output + 1}/{_preferences.Output + 2}";
        string Db(float peak) => peak > .00001f ? $"{20 * Math.Log10(peak):0.0} dB" : "無音";
        OutputTestButton.IsEnabled = _connected && _preferences.MonitorOutput && !_audio.IsOutputTestActive;
        string buffers = $"ASIO {_audio.AsioBufferFrames} samples" + (_preferences.PcOutput && _preferences.MonitorOutput
            ? $" / PC要求 {_audio.PcBufferMilliseconds} ms / キュー {_audio.PcQueuedMilliseconds:0.0} ms" : "");
        AudioStatusText.Text = $"{buffers} / {_preferences.Rate / 1000.0:0.#} kHz / INPUT {_preferences.Input + 1} / {state} / {output} / IN {Db(_audio.InputPeak)} / OUT {Db(_audio.OutputPeak)}";
        AudioStatusText.ToolTip = AudioStatusText.Text;
    }
    private void ConnectAudio()
    {
        if (_preferences is null) return;
        _connected = false; Interlocked.Exchange(ref _receivedBlocks, 0);
        try
        {
            _audio.Start(_preferences.Driver, _preferences.Input, _preferences.Rate,
                _preferences.MonitorOutput, _preferences.Output, _preferences.PcOutput, _preferences.PcDeviceId,
                _preferences.PcLatencyMs);
            _connected = true; RefreshAudioStatus(); _chord.NotifyAudioChanged();
        }
        catch (Exception ex)
        {
            AudioStatusText.Text = "共通音声入力: 接続できません / " + ex.Message;
            OutputTestButton.IsEnabled = false;
            _chord.NotifyAudioChanged();
            MessageBox.Show(this, ex.Message + "\n⚙から入力・出力機器を確認してください。", "音声を接続できません");
        }
    }
    private void AudioSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_chord.IsRecording)
            {
                MessageBox.Show(this, "コード検出の録音を停止してから音声入力を切り替えてください。", "録音中");
                return;
            }
            _audio.Stop(); _connected = false; _chord.NotifyAudioChanged(); AudioStatusText.Text = "共通音声入力: 設定中";
            var dialog = new AudioSettingsWindow(_preferences) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                _preferences = dialog.Preferences;
                // 設定保存の失敗で入力の開始を妨げないようにします。
                try { _preferences!.Save(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "音声設定の保存"); }
            }
            if (_preferences is null) AudioStatusText.Text = "共通音声入力: 未接続（⚙から設定）";
            else ConnectAudio();
        }
        catch (Exception ex) { AudioStatusText.Text = "共通音声入力: " + ex.Message; }
    }
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_closeApproved)
        {
            e.Cancel = true;
            if (_checkingClose) return;
            _checkingClose = true;
            try
            {
                if (_chord.IsRecording)
                {
                    MessageBox.Show(this, "コード検出の録音を停止してからアプリを終了してください。", "録音中");
                    return;
                }
                if (await _drum.CanCloseAsync())
                {
                    _closeApproved = true;
                    // Closingイベントが終了してから再度閉じます。
                    _ = Dispatcher.BeginInvoke(new Action(Close));
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "終了できません"); }
            finally { _checkingClose = false; }
            return;
        }
        _timer.Stop();
        try { _chord.Dispose(); _audio.Dispose(); _tuner.Dispose(); _drum.Dispose(); }
        catch (Exception ex)
        {
            e.Cancel = true; _closeApproved = false;
            AudioStatusText.Text = "終了処理: " + ex.Message; _timer.Start();
        }
    }
    private void OutputTest_Click(object sender, RoutedEventArgs e)
    {
        try { _audio.StartOutputTest(); OutputTestButton.IsEnabled = false; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "出力テスト"); }
    }
    // Only capture number keys when the Footswitch page is showing. Other pages,
    // ComboBoxes and text inputs continue to receive their normal keystrokes.
    private void HandleFootswitchKeys(object sender, KeyEventArgs e)
    {
        if (ReferenceEquals(MainContent.Content, _foot) && _foot.TryHandleKey(e))
            e.Handled = true;
    }

    private FootswitchActionResult ExecuteFootswitchAction(string action, int presetNumber)
    {
        if (action is "Distortion ON/OFF" or "Drive B ON/OFF" or "AMP ON/OFF")
        {
            string slot = action switch
            {
                "Distortion ON/OFF" => "DRIVE A",
                "Drive B ON/OFF" => "DRIVE B",
                _ => "AMP"
            };
            if (_tone.ToggleFootswitchEffect(slot, out bool enabled))
                return new FootswitchActionResult($"{slot} {(enabled ? "ON" : "OFF")}", enabled);
            return new FootswitchActionResult($"{slot}: 利用できません");
        }

        int next = action switch
        {
            "Next Preset" => presetNumber % 5 + 1,
            "Previous Preset" => (presetNumber + 3) % 5 + 1,
            "Preset 1" => 1, "Preset 2" => 2, "Preset 3" => 3,
            "Preset 4" => 4, "Preset 5" => 5,
            _ => 0
        };
        if (next > 0)
        {
            if (!_tone.ApplyFootswitchPreset(next))
                return new FootswitchActionResult("音色切替に失敗しました");
            return new FootswitchActionResult($"PRESET {next} SELECTED", true, next);
        }
        if (action == "Tuner")
        {
            ShowPage("Tuner");
            return new FootswitchActionResult("TUNER OPENED", true);
        }
        if (action is "Delay ON/OFF" or "Reverb ON/OFF" or "Chorus ON/OFF")
            return new FootswitchActionResult($"{action.Replace(" ON/OFF", "")}: 未実装（音は変わりません）");
        return new FootswitchActionResult("UNASSIGNED");
    }

    private void ToneNav_Click(object sender, RoutedEventArgs e) => ShowPage("Tone");
    private void TunerNav_Click(object sender, RoutedEventArgs e) => ShowPage("Tuner");
    private void ChordNav_Click(object sender, RoutedEventArgs e) => ShowPage("Chord");
    private void DrumNav_Click(object sender, RoutedEventArgs e) => ShowPage("Drum");
    private void FootswitchNav_Click(object sender, RoutedEventArgs e) => ShowPage("Footswitch");
    private void ShowPage(string page)
    {
        bool drumPage = page == "Drum";
        ShellRoot.RowDefinitions[0].Height = new GridLength(drumPage ? 0 : 76);
        ShellRoot.RowDefinitions[2].Height = new GridLength(drumPage ? 0 : 36);
        ShellBody.ColumnDefinitions[0].Width = new GridLength(drumPage ? 0 : 126);
        ShellHeader.Visibility = ShellSidebar.Visibility = ShellFooter.Visibility =
            drumPage ? Visibility.Collapsed : Visibility.Visible;
        Title = drumPage ? "楽フェクター — " + _drum.EditorTitle : "楽フェクター";
        MainContent.Content = page switch
        {
            "Tuner" => (UserControl)_tuner, "Chord" => _chord,
            "Drum" => _drum, "Footswitch" => _foot, _ => _tone
        };
        PageTitleText.Text = page switch
        {
            "Tuner" => "TUNER", "Chord" => "CHORD DETECTION", "Drum" => "DRUM EDITOR",
            "Footswitch" => "FOOTSWITCH SETTING", _ => "TONE STUDIO"
        };
        var buttons = new[] { ToneNavButton, TunerNavButton, ChordNavButton, DrumNavButton, FootswitchNavButton };
        var names = new[] { "Tone", "Tuner", "Chord", "Drum", "Footswitch" };
        for (int i = 0; i < buttons.Length; i++)
        {
            bool active = page == names[i];
            buttons[i].Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#101214" : "#17191C"));
            buttons[i].Foreground = active ? Brushes.White : Brushes.SlateGray;
            buttons[i].FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        }
    }
    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "⚙で入力用ASIOドライバー・入力番号・周波数を指定します。「ギター音を出力する」をONにし、PCのスピーカーまたはASIO機器を選んで接続してください。PC出力では、使うスピーカーを『PCの出力機器』から選びます。『出力テスト』で選択した機器から3秒間の確認音を鳴らせます。PC出力ではWindowsの音量・ミュートも確認してください。設定は保存され、画面を切り替えても入力と出力は続きます。",
        "共通音声入出力");
}
