using System.Text.Json;
namespace DrumEditor.Files;

public sealed record UiSettings(double Left,double Top,double Width,double Height,bool Maximized,double VelocityRatio,string? MidiFolder, int ZoomPercent = 100);
public static class UiSettingsStore
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DrumEditor","ui-settings.json");
    public static UiSettings? Load(string path)
    {
        try
        {
            if(!File.Exists(path)||new FileInfo(path).Length>65536)return null;
            var s=JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path));
            return s is not null && double.IsFinite(s.Left)&&double.IsFinite(s.Top)&&double.IsFinite(s.Width)&&double.IsFinite(s.Height)&&double.IsFinite(s.VelocityRatio)&&s.Width>=1000&&s.Height>=700&&s.Width<=20000&&s.Height<=20000&&s.VelocityRatio is >0 and <1?s:null;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException){return null;}
    }
    public static void Save(string path,UiSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+"."+Guid.NewGuid()+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(settings));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
