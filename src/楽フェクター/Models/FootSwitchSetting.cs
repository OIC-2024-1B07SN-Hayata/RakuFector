using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FXForge.Models;

// One assignable, clickable footswitch. Changes immediately update its WPF card.
public sealed class FootSwitchSetting : INotifyPropertyChanged
{
    private string _assignment = "-- NONE --";
    private bool _isLit;
    private string _status = "UNASSIGNED";

    public int Number { get; set; }
    [JsonIgnore]
    public string KeyLabel => $"F{Number}";
    [JsonIgnore]
    public string Label => $"SW-{Number}";

    public string Assignment
    {
        get => _assignment;
        set
        {
            if (_assignment == value) return;
            _assignment = value;
            IsLit = false;
            Status = value == "-- NONE --" ? "UNASSIGNED" : "READY";
            Notify();
        }
    }

    public bool IsLit
    {
        get => _isLit;
        set { if (_isLit == value) return; _isLit = value; Notify(); }
    }

    public string Status
    {
        get => _status;
        set { if (_status == value) return; _status = value; Notify(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// File shape matches the original WinForms footswitch_settings.json.
public sealed class FootSwitchSettingsFile
{
    public int PhysicalCount { get; set; } = 2;
    public List<FootSwitchSetting> Switches { get; set; } = new();
}

public sealed record FootswitchActionResult(string Status, bool IsLit = false, int? PresetNumber = null);
