using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FXForge.Models;

public class EffectParameter : INotifyPropertyChanged
{
    private double _value;
    private double _step;
    private string[]? _labels;
    private bool _isEditable = true;

    public EffectParameter(
        string name,
        double value,
        double min = 0,
        double max = 100,
        string unit = "")
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

    public double Step => _step;

    public string? KnobText =>
        _labels is null
            ? null
            : _labels[(int)Math.Round(Value)];

    public bool IsEditable
    {
        get => _isEditable;
        set
        {
            if (_isEditable == value)
                return;

            _isEditable = value;
            OnPropertyChanged();
        }
    }

    public void SetDiscreteValues(
        double step,
        string[]? labels = null)
    {
        if (!double.IsFinite(step) || step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step));

        if (labels is not null &&
            (Minimum != 0 || Maximum != labels.Length - 1))
        {
            throw new ArgumentException(
                "選択肢の数とノブの範囲が一致しません。");
        }

        _step = step;
        _labels = labels is null
            ? null
            : (string[])labels.Clone();

        Value =
            Math.Round(Value / step, MidpointRounding.AwayFromZero) *
            step;

        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(KnobText));
    }

    public double Value
    {
        get => _value;
        set
        {
            if (!double.IsFinite(value))
                return;

            if (_step > 0)
            {
                value =
                    Math.Round(
                        value / _step,
                        MidpointRounding.AwayFromZero) *
                    _step;
            }

            value = Math.Clamp(value, Minimum, Maximum);

            if (Math.Abs(_value - value) < 0.001)
                return;

            _value = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(KnobText));
        }
    }

    public string DisplayValue => $"{Value:0}{Unit}";

    public double Angle
    {
        get
        {
            if (Math.Abs(Maximum - Minimum) < 0.001)
                return -135;

            double ratio =
                (Value - Minimum) / (Maximum - Minimum);

            return -135 + ratio * 270;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));

        if (propertyName == nameof(Value))
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(DisplayValue)));

            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(Angle)));
        }
    }
}