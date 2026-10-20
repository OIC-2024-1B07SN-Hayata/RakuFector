using FXForge.AudioEffects;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using FXForge.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static FXForge.Common.UiBrushes;

namespace FXForge.Views;

public partial class TonePage : UserControl, INotifyPropertyChanged
{
    private EffectItem? _selectedEffect;
    private readonly ToneAudioEngine _engine = new();
    private bool _applyingPreset;
    public void ProcessAudioOutput(float[] samples, int sampleRate) => _engine.Process(samples, sampleRate);
    public void ResetAudio() => _engine.Reset();
    public void ReceiveAudio(float[] samples, int sampleRate, int channels) { /* 元の入力の受け取り口 */ }

    public ObservableCollection<EffectItem> Effects { get; } = new();

    public EffectItem? SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            if (_selectedEffect == value) return;
            _selectedEffect = value;
            OnPropertyChanged();
        }
    }

    public TonePage()
    {
        InitializeComponent();
        BuildEffects();
        SelectedEffect = Effects.FirstOrDefault(e => e.SlotName == "DRIVE A");
        DataContext = this;
        Effects.CollectionChanged += (_, _) => { ObserveParameters(); SynchronizeAudioSettings(); };
        foreach (var effect in Effects)
        {
            effect.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(EffectItem.IsEnabled) or "ParametersChanged")
                    SynchronizeAudioSettings();
            };
            effect.Parameters.CollectionChanged += (_, _) => { ObserveParameters(); SynchronizeAudioSettings(); };
        }
        ObserveParameters();
        SynchronizeAudioSettings();
    }

    private void BuildEffects()
    {
        var input = new EffectItem("INPUT", "IN", "Guitar Input", "INPUT", "#27D69B", true, false, false);

        var defaults = ToneSettings.Default;
        var drivePresets = new[]
        {
            new EffectPresetDefinition("SD-1", "SD-1 Style / Prototype", "#E9B21C",
                new ParameterDefinition("Drive", defaults.A.Drive * 100),
                new ParameterDefinition("Tone", defaults.A.Tone * 100),
                new ParameterDefinition("Level", defaults.A.Level * 100)),
            new EffectPresetDefinition("RAT", "RAT Style / Prototype", "#C85A7A",
                new ParameterDefinition("Distortion", defaults.B.Drive * 100),
                new ParameterDefinition("Filter", defaults.B.Tone * 100),
                new ParameterDefinition("Volume", defaults.B.Level * 100))
        };
        var driveA = CreateDriveSlot("DRIVE A", drivePresets, 0);
        var driveB = CreateDriveSlot("DRIVE B", drivePresets, 1);
        driveA.IsEnabled = driveB.IsEnabled = false;
        var amp = new EffectItem("AMP", "AMP", "Generic Guitar Amp / Prototype", "AMP", "#4C8CF5", true);
        amp.SetFixedParameters(new EffectParameter("Gain", defaults.Amp.Gain * 100), new EffectParameter("Bass", defaults.Amp.Bass * 100),
            new EffectParameter("Middle", defaults.Amp.Middle * 100), new EffectParameter("Treble", defaults.Amp.Treble * 100), new EffectParameter("Level", defaults.Amp.Level * 100));
        var output = new EffectItem("OUTPUT", "OUT", "Output Volume", "OUTPUT", "#9AA4B2", true);
        output.SetFixedParameters(new EffectParameter("Level", defaults.OutputLevel * 100));
        Effects.Add(input); Effects.Add(driveA); Effects.Add(driveB); Effects.Add(amp); Effects.Add(output);
    }

    private static EffectItem CreateDriveSlot(string slotName,
        IEnumerable<EffectPresetDefinition> presets, int selectedIndex)
    {
        var effect = new EffectItem(slotName, "DRV", "Drive", "DRIVE", "#E9B21C", true, true);
        foreach (var preset in presets) effect.AvailablePresets.Add(preset);
        effect.SelectedPreset = effect.AvailablePresets[selectedIndex];
        return effect;
    }

    private readonly HashSet<EffectParameter> _watched = new();
    private void ObserveParameters()
    {
        var current = Effects.SelectMany(e => e.Parameters).ToHashSet();
        foreach (var old in _watched.Where(p => !current.Contains(p)).ToArray())
        { old.PropertyChanged -= ParameterChanged; _watched.Remove(old); }
        foreach (var parameter in current)
            if (_watched.Add(parameter)) parameter.PropertyChanged += ParameterChanged;
    }
    private void ParameterChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(EffectParameter.Value)) SynchronizeAudioSettings(); }
    public event Action? DisplaySettingsChanged;

    private static double Value(EffectItem item, string name) =>
        (item.Parameters.FirstOrDefault(p => p.Name == name)?.Value ?? 50) / 100.0;
    private void SynchronizeAudioSettings()
    {
        if (_applyingPreset) return;
        var a = Effects.First(e => e.SlotName == "DRIVE A");
        var b = Effects.First(e => e.SlotName == "DRIVE B");
        var amp = Effects.First(e => e.SlotName == "AMP");
        var output = Effects.First(e => e.SlotName == "OUTPUT");
        DriveSettings Drive(EffectItem e)
        {
            bool rat = e.ShortName == "RAT";
            return new(rat ? DriveKind.Rat : DriveKind.Sd1, e.IsEnabled,
                Value(e, rat ? "Distortion" : "Drive"), Value(e, rat ? "Filter" : "Tone"),
                Value(e, rat ? "Volume" : "Level"));
        }
        _engine.SetSettings(new(Drive(a), Drive(b), Effects.IndexOf(b) < Effects.IndexOf(a),
            new(amp.IsEnabled, Value(amp, "Gain"), Value(amp, "Bass"), Value(amp, "Middle"),
                Value(amp, "Treble"), Value(amp, "Level")), output.IsEnabled ? Value(output, "Level") : 0));
        DisplaySettingsChanged?.Invoke();
    }

    private void QuickPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string mode }) ApplyQuickPreset(mode);
    }

    // The footswitch can control the same live EffectItem objects that the UI edits.
    // EffectItem.IsEnabled changes are already subscribed by SynchronizeAudioSettings().
    public bool ToggleFootswitchEffect(string slotName, out bool enabled)
    {
        var effect = Effects.FirstOrDefault(x => x.SlotName == slotName);
        if (effect is null)
        {
            enabled = false;
            return false;
        }
        effect.IsEnabled = !effect.IsEnabled;
        enabled = effect.IsEnabled;
        return true;
    }

    // Fixed demo sounds (not user-stored patches): 1=raw, 2=amp, 3=SD-1,
    // 4=RAT, 5=SD-1 + RAT. These update the actual audio chain, not just a label.
    public bool ApplyFootswitchPreset(int number)
    {
        string? mode = number switch
        {
            1 => "Raw", 2 => "Amp", 3 => "Sd1", 4 => "Rat", 5 => "Stack", _ => null
        };
        if (mode is null) return false;
        ApplyQuickPreset(mode);
        return true;
    }

    private void ApplyQuickPreset(string mode)
    {
        var a = Effects.First(x => x.SlotName == "DRIVE A");
        var b = Effects.First(x => x.SlotName == "DRIVE B");
        var amp = Effects.First(x => x.SlotName == "AMP");
        var output = Effects.First(x => x.SlotName == "OUTPUT");
        _applyingPreset = true;
        try
        {
            a.SelectedPreset = a.AvailablePresets.First(p => p.ShortName == "SD-1");
            b.SelectedPreset = b.AvailablePresets.First(p => p.ShortName == "RAT");
            foreach (var effect in new[] { a, b })
                foreach (var parameter in effect.Parameters)
                    parameter.Value = effect.SelectedPreset!.Parameters.First(p => p.Name == parameter.Name).DefaultValue;
            a.IsEnabled = mode is "Sd1" or "Stack";
            b.IsEnabled = mode is "Rat" or "Stack";
            amp.IsEnabled = mode != "Raw";
            output.IsEnabled = true;
            var defaults = ToneSettings.Default;
            foreach (var parameter in amp.Parameters)
                parameter.Value = 100 * (parameter.Name switch
                {
                    "Gain" => defaults.Amp.Gain, "Bass" => defaults.Amp.Bass,
                    "Middle" => defaults.Amp.Middle, "Treble" => defaults.Amp.Treble,
                    "Level" => defaults.Amp.Level, _ => .5
                });
            output.Parameters[0].Value = defaults.OutputLevel * 100;
            SelectedEffect = mode == "Sd1" || mode == "Stack" ? a : mode == "Rat" ? b : amp;
        }
        finally { _applyingPreset = false; SynchronizeAudioSettings(); }
    }

    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(
        "上の『原音 / AMP / SD-1+AMP / RAT+AMP』で音を比較できます。\n" +
        "チェーンのカードを選択し、ノブを上下ドラッグ・ホイール・矢印キーで調整します。\n" +
        "ON/OFFとノブの値が音に反映されます。RATのFilterは大きくすると高域が減ります。\n" +
        "歪みA/Bはドラッグで順序を入れ替えできます。OUTPUTのLevelは全体音量です。\n" +
        "SD-1は前版RAT程度の歪み、RATはさらに強い歪みとざらつきを狙っています。\n" +
        "比較用AMPはGain 8。低音弦のブリッジミュートで比べてみてください。\n" +
        "実機回路の忠実な再現ではなく、AMPは汎用モデルです。\n" +
        "音が出ないときは⚙の出力設定と『出力テスト』を確認してください。", "音作り");
    private void EffectButton_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: EffectItem effect } && effect.IsEditable) SelectedEffect = effect; }

    private Point _dragStart;
    private void Effect_MouseDown(object sender, MouseButtonEventArgs e)
        => _dragStart = e.GetPosition(this);

    private void Effect_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not Button { Tag: EffectItem { CanMove: true } effect }) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop((Button)sender, effect, DragDropEffects.Move);
        e.Handled = true;
    }

    private void Effect_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button { Tag: EffectItem { CanMove: true } target } ||
            e.Data.GetData(typeof(EffectItem)) is not EffectItem { CanMove: true } source ||
            ReferenceEquals(source, target)) return;
        int from = Effects.IndexOf(source), to = Effects.IndexOf(target);
        if (from < 0 || to < 0) return;
        Effects.Move(from, to);
        SelectedEffect = source;
        e.Handled = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
