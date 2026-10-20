using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace FXForge.Models;

public class EffectItem : INotifyPropertyChanged
{
    private bool _isEnabled;
    private EffectPresetDefinition? _selectedPreset;
    private string _shortName;
    private string _name;
    private string _colorHex;

    public EffectItem(
        string slotName,
        string shortName,
        string name,
        string category,
        string colorHex,
        bool isEnabled = true,
        bool canMove = false,
        bool isEditable = true)
    {
        SlotName = slotName;
        _shortName = shortName;
        _name = name;
        Category = category;
        _colorHex = colorHex;
        _isEnabled = isEnabled;
        CanMove = canMove;
        IsEditable = isEditable;
        Parameters = new ObservableCollection<EffectParameter>();
        AvailablePresets = new ObservableCollection<EffectPresetDefinition>();
    }

    public string SlotName { get; }
    public string Category { get; }
    public bool CanMove { get; }
    public bool IsEditable { get; }
    public ObservableCollection<EffectParameter> Parameters { get; }
    public ObservableCollection<EffectPresetDefinition> AvailablePresets { get; }

    public string ShortName
    {
        get => _shortName;
        private set
        {
            if (_shortName == value) return;
            _shortName = value;
            OnPropertyChanged();
        }
    }

    public string Name
    {
        get => _name;
        private set
        {
            if (_name == value) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public string ColorHex
    {
        get => _colorHex;
        private set
        {
            if (_colorHex == value) return;
            _colorHex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AccentBrush));
            OnPropertyChanged(nameof(ActiveAccentBrush));
            OnPropertyChanged(nameof(AccentBrushMuted));
        }
    }

    public bool HasTypeChoices => AvailablePresets.Count > 1;

    public EffectPresetDefinition? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (_selectedPreset == value) return;
            _selectedPreset = value;
            OnPropertyChanged();
            if (value is not null)
                ApplyPreset(value);
        }
    }

    public Brush AccentBrush => CreateBrush(ColorHex);

    // OFF時はアクセント色を消してグレー表示にする。
    public Brush ActiveAccentBrush => IsEnabled ? CreateBrush(ColorHex) : CreateBrush("#59616C");

    public Brush AccentBrushMuted
    {
        get
        {
            var color = (Color)ColorConverter.ConvertFromString(ColorHex)!;
            return new SolidColorBrush(Color.FromArgb(IsEnabled ? (byte)50 : (byte)18, color.R, color.G, color.B));
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActiveAccentBrush));
            OnPropertyChanged(nameof(AccentBrushMuted));
        }
    }

    public void SetFixedParameters(params EffectParameter[] parameters)
    {
        Parameters.Clear();
        foreach (var parameter in parameters)
            Parameters.Add(parameter);
    }

    private void ApplyPreset(EffectPresetDefinition preset)
    {
        ShortName = preset.ShortName;
        Name = preset.Name;
        ColorHex = preset.ColorHex;

        Parameters.Clear();
        foreach (var parameter in preset.Parameters)
        {
            Parameters.Add(new EffectParameter(
                parameter.Name,
                parameter.DefaultValue,
                parameter.Minimum,
                parameter.Maximum,
                parameter.Unit));
        }
        OnPropertyChanged("ParametersChanged");
    }

    private static Brush CreateBrush(string colorHex)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorHex)!;
        return new SolidColorBrush(color);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
