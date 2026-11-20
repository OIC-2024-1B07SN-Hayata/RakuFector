using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FXForge.AudioEffects;
using FXForge.Models;

namespace FXForge.Views;

public partial class TonePage :
    UserControl,
    INotifyPropertyChanged
{
    private readonly ToneAudioEngine _engine = new();
    private readonly HashSet<EffectParameter> _watched = new();

    private EffectItem? _selectedEffect;
    private bool _applyingPreset;
    private Point _dragStart;

    public ObservableCollection<EffectItem> Effects { get; } = new();

    public EffectItem? SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            if (_selectedEffect == value)
                return;

            _selectedEffect = value;
            OnPropertyChanged();
        }
    }

    public event Action? DisplaySettingsChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public TonePage()
    {
        InitializeComponent();
        BuildEffects();

        SelectedEffect = Effects.FirstOrDefault(
            effect => effect.SlotName == "DRIVE A");

        DataContext = this;

        foreach (var effect in Effects)
            effect.PropertyChanged += EffectChanged;

        Effects.CollectionChanged += (_, _) =>
        {
            ObserveParameters();
            SynchronizeAudioSettings();
        };

        ObserveParameters();
        SynchronizeAudioSettings();
    }

    public void ProcessAudioOutput(float[] samples, int sampleRate)
    {
        _engine.Process(samples, sampleRate);
    }

    public void ResetAudio()
    {
        _engine.Reset();
    }

    public void SetTemporaryBypass(bool enabled)
    {
        _engine.SetTemporaryBypass(enabled);
    }

    public void ReceiveAudio(
        float[] samples,
        int sampleRate,
        int channels)
    {
        // 音作りはProcessAudioOutputで処理する。
    }

    private void BuildEffects()
    {
        var defaults = ToneSettings.Default;

        var input = new EffectItem(
            "INPUT",
            "IN",
            "Guitar Input",
            "INPUT",
            "#27D69B",
            true,
            false,
            false);

        var presets = new[]
        {
            new EffectPresetDefinition(
                "SD-1",
                "SD-1 Style / Prototype",
                "#E9B21C",
                new ParameterDefinition(
                    "Drive", defaults.A.Drive * 100),
                new ParameterDefinition(
                    "Tone", defaults.A.Tone * 100),
                new ParameterDefinition(
                    "Level", defaults.A.Level * 100)),

            new EffectPresetDefinition(
                "RAT",
                "RAT Style / Prototype",
                "#C85A7A",
                new ParameterDefinition(
                    "Distortion", defaults.B.Drive * 100),
                new ParameterDefinition(
                    "Filter", defaults.B.Tone * 100),
                new ParameterDefinition(
                    "Volume", defaults.B.Level * 100)),

            new EffectPresetDefinition(
                "TS9",
                "TS9 Style / Prototype",
                "#37B85A",
                new ParameterDefinition("Drive", 50),
                new ParameterDefinition("Tone", 50),
                new ParameterDefinition("Level", 52)),

            new EffectPresetDefinition(
                "BD-2",
                "BD-2 Style / Prototype",
                "#428BE8",
                new ParameterDefinition("Gain", 45),
                new ParameterDefinition("Tone", 50),
                new ParameterDefinition("Level", 52)),

            new EffectPresetDefinition(
                "DS-1",
                "DS-1 Style / Prototype",
                "#F28C28",
                new ParameterDefinition("Distortion", 45),
                new ParameterDefinition("Tone", 50),
                new ParameterDefinition("Level", 52))
        };

        var driveA = CreateDriveSlot("DRIVE A", presets, 0);
        var driveB = CreateDriveSlot("DRIVE B", presets, 1);

        driveA.IsEnabled = false;
        driveB.IsEnabled = false;

        var amp = new EffectItem(
            "AMP",
            "AMP",
            "Clean Amp / Treble & High",
            "AMP",
            "#4C8CF5",
            true);

        amp.SetFixedParameters(
            new EffectParameter(
                "Gain", defaults.Amp.Gain * 100),
            new EffectParameter(
                "Bass", defaults.Amp.Bass * 100),
            new EffectParameter(
                "Middle", defaults.Amp.Middle * 100),
            new EffectParameter(
                "Treble", defaults.Amp.Treble * 100),
            new EffectParameter(
                "High", defaults.Amp.High * 100),
            new EffectParameter(
                "Level", defaults.Amp.Level * 100));

        var output = new EffectItem(
            "OUTPUT",
            "OUT",
            "Output Volume",
            "OUTPUT",
            "#9AA4B2",
            true);

        output.SetFixedParameters(
            new EffectParameter(
                "Level", defaults.OutputLevel * 100));

        Effects.Add(input);
        Effects.Add(driveA);
        Effects.Add(driveB);
        Effects.Add(amp);
        Effects.Add(output);
    }

    private static EffectItem CreateDriveSlot(
        string slotName,
        IEnumerable<EffectPresetDefinition> presets,
        int selectedIndex)
    {
        var effect = new EffectItem(
            slotName,
            "DRV",
            "Drive",
            "DRIVE",
            "#E9B21C",
            true,
            true);

        foreach (var preset in presets)
            effect.AvailablePresets.Add(preset);

        effect.SelectedPreset =
            effect.AvailablePresets[selectedIndex];

        return effect;
    }

    private void EffectChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "ParametersChanged")
        {
            ObserveParameters();
            SynchronizeAudioSettings();
        }
        else if (e.PropertyName == nameof(EffectItem.IsEnabled))
        {
            SynchronizeAudioSettings();
        }
    }

    private void ObserveParameters()
    {
        var current = Effects
            .SelectMany(effect => effect.Parameters)
            .ToHashSet();

        foreach (var old in _watched
                     .Where(parameter => !current.Contains(parameter))
                     .ToArray())
        {
            old.PropertyChanged -= ParameterChanged;
            _watched.Remove(old);
        }

        foreach (var parameter in current)
        {
            if (_watched.Add(parameter))
                parameter.PropertyChanged += ParameterChanged;
        }
    }

    private void ParameterChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EffectParameter.Value))
            SynchronizeAudioSettings();
    }

    private static double Value(
        EffectItem effect,
        string parameterName)
    {
        double value = effect.Parameters
            .FirstOrDefault(parameter => parameter.Name == parameterName)
            ?.Value ?? 50;

        return DriveStage.Unit(value / 100.0);
    }

    private static DriveSettings ReadDrive(EffectItem effect)
    {
        var mapping = effect.ShortName switch
        {
            "SD-1" => (
                Kind: DriveKind.Sd1,
                Drive: "Drive",
                Tone: "Tone",
                Level: "Level"),

            "RAT" => (
                Kind: DriveKind.Rat,
                Drive: "Distortion",
                Tone: "Filter",
                Level: "Volume"),

            "TS9" => (
                Kind: DriveKind.Ts9,
                Drive: "Drive",
                Tone: "Tone",
                Level: "Level"),

            "BD-2" => (
                Kind: DriveKind.Bd2,
                Drive: "Gain",
                Tone: "Tone",
                Level: "Level"),

            "DS-1" => (
                Kind: DriveKind.Ds1,
                Drive: "Distortion",
                Tone: "Tone",
                Level: "Level"),

            _ => throw new InvalidOperationException(
                $"未対応のエフェクト種類です: {effect.ShortName}")
        };

        return new DriveSettings(
            mapping.Kind,
            effect.IsEnabled,
            Value(effect, mapping.Drive),
            Value(effect, mapping.Tone),
            Value(effect, mapping.Level));
    }

    private void SynchronizeAudioSettings()
    {
        if (_applyingPreset)
            return;

        var a = Effects.First(
            effect => effect.SlotName == "DRIVE A");

        var b = Effects.First(
            effect => effect.SlotName == "DRIVE B");

        var amp = Effects.First(
            effect => effect.SlotName == "AMP");

        var output = Effects.First(
            effect => effect.SlotName == "OUTPUT");

        var ampSettings = new AmpSettings(
            amp.IsEnabled,
            Value(amp, "Gain"),
            Value(amp, "Bass"),
            Value(amp, "Middle"),
            Value(amp, "Treble"),
            Value(amp, "Level"))
        {
            High = Value(amp, "High")
        };

        var settings = new ToneSettings(
            ReadDrive(a),
            ReadDrive(b),
            Effects.IndexOf(b) < Effects.IndexOf(a),
            ampSettings,
            output.IsEnabled
                ? Value(output, "Level")
                : 0);

        _engine.SetSettings(settings);
        DisplaySettingsChanged?.Invoke();
    }

    public bool ToggleFootswitchEffect(
        string slotName,
        out bool enabled)
    {
        var effect = Effects.FirstOrDefault(
            item => item.SlotName == slotName);

        if (effect is null)
        {
            enabled = false;
            return false;
        }

        effect.IsEnabled = !effect.IsEnabled;
        enabled = effect.IsEnabled;

        return true;
    }

    public bool ApplyFootswitchPreset(int number)
    {
        string? mode = number switch
        {
            1 => "Raw",
            2 => "Amp",
            3 => "Sd1",
            4 => "Rat",
            5 => "Stack",
            _ => null
        };

        if (mode is null)
            return false;

        ApplyQuickPreset(mode);
        return true;
    }

    private static void ResetDrivePreset(
        EffectItem effect,
        string shortName)
    {
        var preset = effect.AvailablePresets.First(
            item => item.ShortName == shortName);

        effect.SelectedPreset = preset;

        foreach (var parameter in effect.Parameters)
        {
            parameter.Value = preset.Parameters
                .First(item => item.Name == parameter.Name)
                .DefaultValue;
        }
    }

    private void ApplyQuickPreset(string mode)
    {
        var a = Effects.First(
            effect => effect.SlotName == "DRIVE A");

        var b = Effects.First(
            effect => effect.SlotName == "DRIVE B");

        var amp = Effects.First(
            effect => effect.SlotName == "AMP");

        var output = Effects.First(
            effect => effect.SlotName == "OUTPUT");

        _applyingPreset = true;

        try
        {
            ResetDrivePreset(a, "SD-1");
            ResetDrivePreset(b, "RAT");

            a.IsEnabled = mode is "Sd1" or "Stack";
            b.IsEnabled = mode is "Rat" or "Stack";

            amp.IsEnabled = mode != "Raw";
            output.IsEnabled = true;

            var defaults = ToneSettings.Default;

            foreach (var parameter in amp.Parameters)
            {
                parameter.Value = 100 *
                    (parameter.Name switch
                    {
                        "Gain" => defaults.Amp.Gain,
                        "Bass" => defaults.Amp.Bass,
                        "Middle" => defaults.Amp.Middle,
                        "Treble" => defaults.Amp.Treble,
                        "High" => defaults.Amp.High,
                        "Level" => defaults.Amp.Level,
                        _ => .5
                    });
            }

            output.Parameters[0].Value =
                defaults.OutputLevel * 100;

            SelectedEffect = mode switch
            {
                "Sd1" or "Stack" => a,
                "Rat" => b,
                _ => amp
            };
        }
        finally
        {
            _applyingPreset = false;
            ObserveParameters();
            SynchronizeAudioSettings();
        }
    }

    private void Help_Click(
        object sender,
        RoutedEventArgs e)
    {
        MessageBox.Show(
            "DRIVE AまたはDRIVE Bを選択して、\n" +
            "エフェクト種類を変更できます。\n\n" +
            "対応機種：SD-1 / RAT / TS9 / BD-2 / DS-1\n\n" +
            "選択したエフェクトをONにすると音に反映されます。\n" +
            "ノブは上下ドラッグ、ホイール、矢印キーで調整できます。\n\n" +
            "RATのFilterは上げるほど暗くなります。\n" +
            "他の機種のToneは上げるほど明るくなります。\n\n" +
            "AMPのTrebleは3.2kHzを基準に高域を調整します。\n" +
            "AMPのHighは6kHz付近の高域を調整します。\n" +
            "どちらも50が補正なしの位置です。\n\n" +
            "DRIVE A/Bはドラッグで順序を交換できます。\n" +
            "種類を変更するとノブは初期値に戻ります。\n" +
            "OUTPUTのLevelは全体の音量です。\n\n" +
            "ユーザー音色のDB保存・読込はまだ接続していません。",
            "音作り");
    }

    private void EffectButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button { Tag: EffectItem effect } &&
            effect.IsEditable)
        {
            SelectedEffect = effect;
        }
    }

    private void Effect_MouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
    }

    private void Effect_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not Button
            {
                Tag: EffectItem { CanMove: true } effect
            })
        {
            return;
        }

        var position = e.GetPosition(this);

        if (Math.Abs(position.X - _dragStart.X)
                < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y)
                < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(
            (Button)sender,
            effect,
            DragDropEffects.Move);

        e.Handled = true;
    }

    private void Effect_Drop(
        object sender,
        DragEventArgs e)
    {
        if (sender is not Button
            {
                Tag: EffectItem { CanMove: true } target
            } ||
            e.Data.GetData(typeof(EffectItem))
                is not EffectItem { CanMove: true } source ||
            ReferenceEquals(source, target))
        {
            return;
        }

        int from = Effects.IndexOf(source);
        int to = Effects.IndexOf(target);

        if (from < 0 || to < 0)
            return;

        Effects.Move(from, to);
        SelectedEffect = source;

        e.Handled = true;
    }

    protected virtual void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}