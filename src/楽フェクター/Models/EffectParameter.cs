using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FXForge.Models;

public class EffectParameter : INotifyPropertyChanged
{
    private double _value;

    public EffectParameter(string name, double value, double min = 0, double max = 100, string unit = "")
    {
        Name = name;
        _value = value;
        Minimum = min;
        Maximum = max;
        Unit = unit;
    }

    public string Name { get; }
    public double Minimum { get; }
    public double Maximum { get; }
    public string Unit { get; }

    public double Value
    {
        get => _value;
        set
        {
            value=Math.Clamp(value,Minimum,Maximum);
            if (Math.Abs(_value - value) < 0.001) return;
            _value = value;
            OnPropertyChanged();
        }
    }

    public string DisplayValue => $"{Value:0}{Unit}";

    public double Angle
    {
        get
        {
            if (Math.Abs(Maximum - Minimum) < 0.001) return -135;
            var ratio = (Value - Minimum) / (Maximum - Minimum);
            return -135 + (ratio * 270);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        if (propertyName == nameof(Value))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayValue)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Angle)));
        }
    }
}
