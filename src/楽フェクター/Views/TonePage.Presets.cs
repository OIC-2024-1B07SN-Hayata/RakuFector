using System;
using System.Collections.Generic;
using System.Linq;
using FXForge.AudioEffects;
using FXForge.Data;
using FXForge.Models;

namespace FXForge.Views;

public partial class TonePage
{
    public SavedTone CapturePreset() =>
        CapturePresetCore(true);

    private SavedTone CapturePresetCore(
        bool rejectInvalidTiming)
    {
        Dispatcher.VerifyAccess();

        EffectItem a = Slot("DRIVE A");
        EffectItem b = Slot("DRIVE B");
        EffectItem amp = Slot("AMP");
        EffectItem output = Slot("OUTPUT");

        var delaySettings =
            ReadDelay(Slot("DELAY"));

        if (rejectInvalidTiming &&
            !_delayTimingValid)
        {
            throw new InvalidOperationException(
                _delayTimingStatus);
        }

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
            Value(output, "Level"))
        {
            Delay = delaySettings
        };

        var result = new SavedTone(
            settings,
            output.IsEnabled);

        RakufekutaDatabase.Validate(result);

        return result;
    }

    public void ApplySavedPreset(
        SavedTone preset)
    {
        Dispatcher.VerifyAccess();

        RakufekutaDatabase.Validate(preset);
        VerifyPresetUi();

        SavedTone previous =
            CapturePresetCore(false);

        _applyingPreset = true;

        try
        {
            try
            {
                ApplyPresetCore(preset);
                _delayRevision++;
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
        foreach (string slot in new[]
        {
            "DRIVE A",
            "DRIVE B"
        })
        {
            var effect = Slot(slot);

            foreach (DriveKind kind in
                     Enum.GetValues<DriveKind>())
            {
                var definition =
                    effect.AvailablePresets.Single(
                        preset =>
                            preset.ShortName == DriveName(kind));

                if (definition.Parameters.Count != 3)
                {
                    throw new InvalidOperationException(
                        "歪みのノブ定義が不正です。");
                }
            }
        }

        foreach (string name in new[]
        {
            "Gain",
            "Bass",
            "Middle",
            "High",
            "Treble",
            "Level"
        })
        {
            _ = Slot("AMP").Parameters.Single(
                parameter => parameter.Name == name);
        }

        foreach (string name in new[]
        {
            "Time",
            "BPM",
            "Note",
            "Feedback",
            "Mix",
            "Level"
        })
        {
            _ = Slot("DELAY").Parameters.Single(
                parameter => parameter.Name == name);
        }

        foreach (DelayMode mode in
                 Enum.GetValues<DelayMode>())
        {
            _ = Slot("DELAY").AvailablePresets.Single(
                preset =>
                    preset.ShortName == mode.ToString());
        }

        _ = Slot("OUTPUT").Parameters.Single(
            parameter => parameter.Name == "Level");
    }

    private void ApplyPresetCore(
        SavedTone preset)
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
                parameter =>
                    parameter.Name == value.Key)
                .Value = value.Value * 100;
        }

        amp.IsEnabled =
            settings.Amp.Enabled;

        var output = Slot("OUTPUT");

        output.Parameters.Single(
            parameter => parameter.Name == "Level")
            .Value = settings.OutputLevel * 100;

        output.IsEnabled =
            preset.OutputEnabled;

        var delay = Slot("DELAY");

        delay.SelectedPreset =
            delay.AvailablePresets.Single(
                definition =>
                    definition.ShortName ==
                    settings.Delay.Mode.ToString());

        _delayManualTimeMs =
            settings.Delay.TimeMilliseconds;

        _delaySelectedNote =
            settings.Delay.Note;

        _delayTempoMode =
            settings.Delay.Note != DelayNote.Manual;

        OnPropertyChanged(nameof(IsTimeMode));
        OnPropertyChanged(nameof(IsTempoMode));

        delay.Parameters.Single(
            parameter => parameter.Name == "BPM")
            .Value = settings.Delay.TempoBpm;

        delay.Parameters.Single(
            parameter => parameter.Name == "Note")
            .Value = settings.Delay.Note == DelayNote.Manual
                ? 3
                : (int)settings.Delay.Note - 1;

        delay.Parameters.Single(
            parameter => parameter.Name == "Time")
            .Value =
                settings.Delay.EffectiveTimeMilliseconds;

        delay.Parameters.Single(
            parameter => parameter.Name == "Feedback")
            .Value = settings.Delay.Feedback * 100;

        delay.Parameters.Single(
            parameter => parameter.Name == "Mix")
            .Value = settings.Delay.Mix * 100;

        delay.Parameters.Single(
            parameter => parameter.Name == "Level")
            .Value = settings.Delay.WetLevel * 100;

        delay.IsEnabled =
            settings.Delay.Enabled;
    }

    private static void ApplyDrive(
        EffectItem effect,
        DriveSettings settings)
    {
        effect.SelectedPreset =
            effect.AvailablePresets.Single(
                preset =>
                    preset.ShortName ==
                    DriveName(settings.Kind));

        string gainName = settings.Kind switch
        {
            DriveKind.Rat or DriveKind.Ds1 =>
                "Distortion",

            DriveKind.Bd2 => "Gain",

            _ => "Drive"
        };

        string toneName =
            settings.Kind == DriveKind.Rat
                ? "Filter"
                : "Tone";

        string levelName =
            settings.Kind == DriveKind.Rat
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

        effect.IsEnabled =
            settings.Enabled;
    }

    private static string DriveName(
        DriveKind kind)
    {
        return kind switch
        {
            DriveKind.Sd1 => "SD-1",
            DriveKind.Rat => "RAT",
            DriveKind.Ts9 => "TS9",
            DriveKind.Bd2 => "BD-2",
            DriveKind.Ds1 => "DS-1",

            _ => throw new ArgumentOutOfRangeException(
                nameof(kind))
        };
    }
}