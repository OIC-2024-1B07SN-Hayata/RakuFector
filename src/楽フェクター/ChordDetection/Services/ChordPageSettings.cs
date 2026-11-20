using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChordDetection.App.Services;

/// <summary>
/// Display settings of the chord detection page kept between runs. Per-song values (capo, transposition),
/// drums and recording / analysis options are not kept.
/// </summary>
public sealed record ChordPageSettings
{
    public double TimelineSeconds { get; init; } = 30;
    public bool ShowDegrees { get; init; }

    /// <summary>"RelativeMajor" (default) or "MinorTonic" (<see cref="ChordDetection.Core.Music.MinorDegreeBase"/>).</summary>
    public string? MinorDegreeBase { get; init; }

    public int LineDivision { get; init; } = 32;

    // Written by earlier versions (コードの表示 with three choices, a check box for the minor base): read only.
    [JsonPropertyName("LabelMode"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyLabelMode { get; init; }

    [JsonPropertyName("MinorTonicDegrees"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyMinorTonicDegrees { get; init; }

    /// <summary>Degrees on, also from an older file whose コードの表示 was ディグリー or コード名＋ディグリー.</summary>
    [JsonIgnore]
    public bool WantsDegrees => ShowDegrees || LegacyLabelMode is "Degree" or "Both";

    /// <summary>The minor base, also from an older file's check box.</summary>
    [JsonIgnore]
    public ChordDetection.Core.Music.MinorDegreeBase MinorBase =>
        Enum.TryParse<ChordDetection.Core.Music.MinorDegreeBase>(MinorDegreeBase, out var value) ? value
        : LegacyMinorTonicDegrees == true ? ChordDetection.Core.Music.MinorDegreeBase.MinorTonic
        : ChordDetection.Core.Music.MinorDegreeBase.RelativeMajor;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FXForgeWpfCommonAudio", "chord-page.json");

    /// <summary>The saved settings; null when there are none or the file cannot be read.</summary>
    public static ChordPageSettings? Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 65536) return null;
            return JsonSerializer.Deserialize<ChordPageSettings>(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the settings (through a temporary file, so a crash never leaves half a file). Errors are ignored.</summary>
    public void Save()
    {
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to keep the settings must not disturb the page.
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }
}
