using System.Text.Json;
using DrumEditor.Domain;
namespace DrumEditor.Files;
public sealed record RecoveryRecord(int FormatVersion,string Session,DateTime SavedUtc,string? OriginalPath,Pattern Pattern);
public static class RecoveryStore
{
    public static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DrumEditor","Recovery");
    public static void Save(string directory,string session,string? path,Pattern pattern)
    {
        if(!Guid.TryParseExact(session,"N",out _))throw new InvalidDataException("セッションが不正です。");
        pattern.Validate();Directory.CreateDirectory(directory);string name=Path.Combine(directory,session+"-"+Guid.NewGuid().ToString("N")+".json");string temp=name+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(new RecoveryRecord(1,session,DateTime.UtcNow,path,pattern),Pattern.JsonOptions));File.Move(temp,name);foreach(var old in Directory.GetFiles(directory,session+"-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Skip(3))File.Delete(old);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static RecoveryRecord[] List(string directory)
    {
        if(!Directory.Exists(directory))return [];
        var records=new List<RecoveryRecord>();foreach(string path in Directory.GetFiles(directory,"*.json"))try{if(new FileInfo(path).Length>10*1024*1024)continue;var r=JsonSerializer.Deserialize<RecoveryRecord>(File.ReadAllText(path),Pattern.JsonOptions);if(r is not null&&r.Pattern is not null&&r.FormatVersion==1&&Guid.TryParseExact(r.Session,"N",out _)){string active=Path.Combine(directory,r.Session+".active");if(File.Exists(active)){try{using var probe=new FileStream(active,FileMode.Open,FileAccess.ReadWrite,FileShare.None);}catch(IOException){continue;}}r.Pattern.Validate();records.Add(r);}}catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException){ }
        return records.OrderByDescending(r=>r.SavedUtc).ToArray();
    }
    public static void Clear(string directory,string session)
    {
        if(!Guid.TryParseExact(session,"N",out _))throw new InvalidDataException("セッションが不正です。");if(Directory.Exists(directory))foreach(string file in Directory.GetFiles(directory,session+"-*.json"))File.Delete(file);
    }
}
