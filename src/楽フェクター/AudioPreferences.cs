using System.IO;
using System.Text.Json;
namespace FXForge;
public sealed record AudioPreferences(string Driver, int Input = 0, int Rate = 48000,
    bool MonitorOutput = true, int Output = 0, bool PcOutput = true,
    string? PcDeviceId = null, string? PcDeviceName = null, int PcLatencyMs = 10)
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FXForgeWpfCommonAudio", "audio.json");
    public static AudioPreferences? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AudioPreferences>(File.ReadAllText(FilePath)) : null;
        }
        catch { return null; }
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}
