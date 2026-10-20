using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FXForge.Models;
using Microsoft.Win32;

namespace FXForge.Views;

/// <summary>
/// WPF port of FXForge_FootSwitch_WinForms/Form1.cs. The original assignment set,
/// physical-count management, simulated pedal buttons, F1-F10 keys, and JSON I/O
/// live here. The shell supplies the actual effect/preset/navigation callbacks.
/// </summary>
public partial class FootswitchPage : UserControl, INotifyPropertyChanged
{
    public string[] Assignments { get; } =
    [
        "-- NONE --", "Delay ON/OFF", "Reverb ON/OFF", "Distortion ON/OFF",
        "Chorus ON/OFF", "Next Preset", "Previous Preset",
        "Preset 1", "Preset 2", "Preset 3", "Preset 4", "Preset 5", "Tuner",
        "Drive B ON/OFF", "AMP ON/OFF"
    ];

    public int[] PhysicalCounts { get; } = Enumerable.Range(1, 10).ToArray();
    public ObservableCollection<FootSwitchSetting> Switches { get; } = new();

    // The actual audio controls and page navigation remain owned by MainWindow.
    public Func<string, int, FootswitchActionResult>? ExecuteAction { get; set; }

    private int _physicalCount = 2;
    public int PhysicalCount
    {
        get => _physicalCount;
        set
        {
            int count = Math.Clamp(value, 1, 10);
            if (_physicalCount == count) return;
            _physicalCount = count;
            while (Switches.Count > count) Switches.RemoveAt(Switches.Count - 1);
            if (Switches.Count == 0) Switches.Add(new FootSwitchSetting { Number = 1 });
            Notify();
            RefreshStatus();
        }
    }

    private int _currentPreset = 2; // Default TonePage has AMP enabled, drives disabled.
    public int CurrentPreset
    {
        get => _currentPreset;
        private set { _currentPreset = value; Notify(); }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; Notify(); }
    }
    public bool CanAdd => Switches.Count < PhysicalCount;
    public bool CanRemove => Switches.Count > 1;

    public FootswitchPage()
    {
        InitializeComponent();
        Switches.Add(new FootSwitchSetting { Number = 1 });
        DataContext = this;
        RefreshStatus();
    }

    private void RefreshStatus(string? message = null)
    {
        Notify(nameof(CanAdd));
        Notify(nameof(CanRemove));
        StatusText = message ?? $"{Switches.Count}/{PhysicalCount} switches  |  Keyboard: F1–F10  |  Preset: {CurrentPreset}";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!CanAdd) return;
        Switches.Add(new FootSwitchSetting { Number = Switches.Count + 1 });
        RefreshStatus();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (!CanRemove) return;
        Switches.RemoveAt(Switches.Count - 1);
        RefreshStatus();
    }

    private void Switch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FootSwitchSetting setting }) PressSwitch(setting);
    }

    private void PressSwitch(FootSwitchSetting setting)
    {
        if (setting.Assignment == "-- NONE --")
        {
            setting.IsLit = false;
            setting.Status = "UNASSIGNED";
            RefreshStatus($"SW-{setting.Number}: UNASSIGNED");
            return;
        }
        try
        {
            var result = ExecuteAction?.Invoke(setting.Assignment, CurrentPreset)
                ?? new FootswitchActionResult("操作先が未接続です");
            setting.Status = result.Status;
            setting.IsLit = result.IsLit;
            if (result.PresetNumber is >= 1 and <= 5) CurrentPreset = result.PresetNumber.Value;
            RefreshStatus($"SW-{setting.Number}: {result.Status}  |  Preset: {CurrentPreset}  |  {Switches.Count}/{PhysicalCount} switches");
        }
        catch (Exception ex)
        {
            setting.IsLit = false;
            setting.Status = "ERROR: " + ex.Message;
            RefreshStatus($"SW-{setting.Number}: {setting.Status}");
        }
    }

    // Faithful WPF adaptation of the updated WinForms ProcessCmdKey (F1-F10).
    // MainWindow dispatches only while the Footswitch page is visible.
    // Like the source WinForms implementation, the physical-count selector retains
    // its normal keyboard operation, and modified shortcuts are not intercepted.
    public bool TryHandleKey(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || PhysicalCountSelector.IsKeyboardFocusWithin)
            return false;

        // WPF sometimes reports F10 as a system key; normalize it for SW-10.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        int number = key switch
        {
            Key.F1 => 1,
            Key.F2 => 2,
            Key.F3 => 3,
            Key.F4 => 4,
            Key.F5 => 5,
            Key.F6 => 6,
            Key.F7 => 7,
            Key.F8 => 8,
            Key.F9 => 9,
            Key.F10 => 10,
            _ => 0
        };
        if (number < 1 || number > Switches.Count) return false;
        PressSwitch(Switches[number - 1]);
        return true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Footswitch settings (*.json)|*.json", FileName = "footswitch_settings.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var configuration = new FootSwitchSettingsFile
            {
                PhysicalCount = PhysicalCount,
                Switches = Switches.ToList()
            };
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(configuration,
                new JsonSerializerOptions { WriteIndented = true }));
            RefreshStatus("フットスイッチの設定を保存しました。");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "保存に失敗しました", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Footswitch settings (*.json)|*.json" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var configuration = JsonSerializer.Deserialize<FootSwitchSettingsFile>(File.ReadAllText(dialog.FileName),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (configuration is null) throw new InvalidDataException("設定ファイルが空です。");
            PhysicalCount = Math.Clamp(configuration.PhysicalCount, 1, 10);
            Switches.Clear();
            foreach (var loaded in (configuration.Switches ?? new()).Take(PhysicalCount))
            {
                if (loaded is null) continue;
                // Avoid stale ON lamps: loading an assignment is not the same as applying an effect.
                var item = new FootSwitchSetting { Number = Switches.Count + 1 };
                item.Assignment = Assignments.Contains(loaded.Assignment) ? loaded.Assignment : "-- NONE --";
                Switches.Add(item);
            }
            if (Switches.Count == 0) Switches.Add(new FootSwitchSetting { Number = 1 });
            RefreshStatus("設定を読み込みました（音作りのON/OFF状態は変更しません）。");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "読込に失敗しました", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(
        "フットスイッチの追加・削除、機能割り当て、クリック／F1～F10キーでの操作、JSON保存・読込に対応しています。\n" +
        "F1～F10を押すと、対応するSW-1～SW-10を操作できます。\n\n" +
        "Distortion＝音作りのDRIVE A、Drive B＝DRIVE B、AMP＝AMPのON/OFFを実際に変更します。\n" +
        "Preset 1～5、Next/Previous Preset は原音／AMP／SD-1／RAT／SD-1+RATの音色を呼び出します。\n" +
        "Tunerはチューナー画面へ移動します。Delay／Reverb／Chorus は音作り側の処理がないため未実装です。\n" +
        "外付けフットスイッチからのUSB/MIDI/HID入力はまだ接続されていません。",
        "フットスイッチ", MessageBoxButton.OK, MessageBoxImage.Information);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
