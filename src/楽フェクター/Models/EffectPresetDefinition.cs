namespace FXForge.Models;

public sealed class EffectPresetDefinition
{
    public EffectPresetDefinition(
        string shortName,
        string name,
        string colorHex,
        params ParameterDefinition[] parameters)
    {
        ShortName = shortName;
        Name = name;
        ColorHex = colorHex;
        Parameters = parameters;
    }

    public string ShortName { get; }
    public string Name { get; }
    public string ColorHex { get; }
    public IReadOnlyList<ParameterDefinition> Parameters { get; }

    public override string ToString() => ShortName;
}

public sealed record ParameterDefinition(
    string Name,
    double DefaultValue,
    double Minimum = 0,
    double Maximum = 100,
    string Unit = "");
