using System;
using System.Collections.Generic;
using System.Linq;
using FXForge.AudioEffects;
using FXForge.Data;
using FXForge.Models;

namespace FXForge.Views;

public partial class TonePage
{
    public SavedTone CapturePreset()
    {
        Dispatcher.VerifyAccess();

        EffectItem a = Slot("DRIVE A");
        EffectItem b = Slot("DRIVE B");
        EffectItem amp = Slot("AMP");
        EffectItem output = Slot("OUTPUT");

        var settings = new ToneSettings(
            ReadDrive(a),
            ReadDrive(b),
            Effects.IndexOf(b) < Effects.IndexOf(a),
            new AmpSettings(
                amp.IsEnabled,
                Value(amp, "Gain"),
                Value(amp, "Bass"),
                Value(amp, "Middle"),
                Value(amp, "Treble"),
                Value(amp, "Level"))
            {
                High = Value(amp, "High")
            },
            Value(output, "Level"));

        var result = new SavedTone(
            settings,
            output.IsEnabled);

        RakufekutaDatabase.Validate(result);

        return result;
    }

    public void ApplySavedPreset(SavedTone preset)
    {
        Dispatcher.VerifyAccess();

        RakufekutaDatabase.Validate(preset);
        VerifyPresetUi();

        SavedTone previous = CapturePreset();

        _applyingPreset = true;

        try
        {
            try
            {
                ApplyPresetCore(preset);
            }
            catch
            {
                ApplyPresetCore(previous);
                throw;
            }
        }
        finally
        {
            _applyingPreset = false;
            ObserveParameters();
            SynchronizeAudioSettings();
        }
    }

    private EffectItem Slot(string name)
    {
        return Effects.First(
            effect => effect.SlotName == name);
    }

    private void VerifyPresetUi()
    {
        foreach (string slot in new[] { "DRIVE A", "DRIVE B" })
        {
            var effect = Slot(slot);

            foreach (DriveKind kind in Enum.GetValues<DriveKind>())
            {
                var definition = effect.AvailablePresets.Single(
                    preset => preset.ShortName == DriveName(kind));

                if (definition.Parameters.Count != 3)
                {
                    throw new InvalidOperationException(
                        "歪みのノブ定義が不正です。");
                }
            }
        }

        foreach (string name in new[]
        {
            "Gain", "Bass", "Middle", "High", "Treble", "Level"
        })
        {
            _ = Slot("AMP").Parameters.Single(
                parameter => parameter.Name == name);
        }

        _ = Slot("OUTPUT").Parameters.Single(
            parameter => parameter.Name == "Level");
    }

    private void ApplyPresetCore(SavedTone preset)
    {
        var settings = preset.Settings;

        EffectItem a = Slot("DRIVE A");
        EffectItem b = Slot("DRIVE B");

        ApplyDrive(a, settings.A);
        ApplyDrive(b, settings.B);

        bool reversed =
            Effects.IndexOf(b) < Effects.IndexOf(a);

        if (reversed != settings.ReverseDrives)
        {
            Effects.Move(
                Effects.IndexOf(a),
                Effects.IndexOf(b));
        }

        var amp = Slot("AMP");

        var values = new Dictionary<string, double>
        {
            ["Gain"] = settings.Amp.Gain,
            ["Bass"] = settings.Amp.Bass,
            ["Middle"] = settings.Amp.Middle,
            ["High"] = settings.Amp.High,
            ["Treble"] = settings.Amp.Treble,
            ["Level"] = settings.Amp.Level
        };

        foreach (var value in values)
        {
            amp.Parameters.Single(
                parameter => parameter.Name == value.Key)
                .Value = value.Value * 100;
        }

        amp.IsEnabled = settings.Amp.Enabled;

        var output = Slot("OUTPUT");

        output.Parameters.Single(
            parameter => parameter.Name == "Level")
            .Value = settings.OutputLevel * 100;

        output.IsEnabled = preset.OutputEnabled;
    }

    private static void ApplyDrive(
        EffectItem effect,
        DriveSettings settings)
    {
        effect.SelectedPreset = effect.AvailablePresets.Single(
            preset => preset.ShortName == DriveName(settings.Kind));

        string gainName = settings.Kind switch
        {
            DriveKind.Rat or DriveKind.Ds1 => "Distortion",
            DriveKind.Bd2 => "Gain",
            _ => "Drive"
        };

        string toneName = settings.Kind == DriveKind.Rat
            ? "Filter"
            : "Tone";

        string levelName = settings.Kind == DriveKind.Rat
            ? "Volume"
            : "Level";

        effect.Parameters.Single(
            parameter => parameter.Name == gainName)
            .Value = settings.Drive * 100;

        effect.Parameters.Single(
            parameter => parameter.Name == toneName)
            .Value = settings.Tone * 100;

        effect.Parameters.Single(
            parameter => parameter.Name == levelName)
            .Value = settings.Level * 100;

        effect.IsEnabled = settings.Enabled;
    }

    private static string DriveName(DriveKind kind)
    {
        return kind switch
        {
            DriveKind.Sd1 => "SD-1",
            DriveKind.Rat => "RAT",
            DriveKind.Ts9 => "TS9",
            DriveKind.Bd2 => "BD-2",
            DriveKind.Ds1 => "DS-1",

            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}