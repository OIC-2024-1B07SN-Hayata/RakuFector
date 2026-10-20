using System.Text.Json;
using DrumEditor.Domain;
namespace DrumEditor.Audio;

public sealed record DrumSample(float[] Stereo, float Gain)
{
    public int Frames => Stereo.Length / 2;
}
public sealed class SampleKit(string id, int sampleRate, DrumSample[] samples)
{
    public string Id { get; } = id;
    public int SampleRate { get; } = sampleRate;
    public DrumSample[] Samples { get; } = samples;
}
public sealed class SampleManifest
{
    public string KitId { get; set; } = "";
    public List<SampleEntry> Instruments { get; set; } = [];
}
public sealed class SampleEntry
{
    public Instrument Id { get; set; }
    public string File { get; set; } = "";
    public double Gain { get; set; } = 1;
}

public static class SampleKitLoader
{
    public const int AssetRate = 44100;
    public static SampleKit Load(string directory, int outputRate = AssetRate)
    {
        if (outputRate is < 8000 or > 192000) throw new InvalidDataException("出力サンプルレートが不正です。");
        string root = Path.GetFullPath(directory);
        string manifestPath = Path.Combine(root, "manifest.json");
        if (new FileInfo(manifestPath).Length > 1024 * 1024) throw new InvalidDataException("音源表が1MiBを超えています。");
        var manifest = JsonSerializer.Deserialize<SampleManifest>(File.ReadAllText(manifestPath), Pattern.JsonOptions)
            ?? throw new InvalidDataException("音源表が空です。");
        if (string.IsNullOrWhiteSpace(manifest.KitId) || manifest.Instruments is null ||
            manifest.Instruments.Count != InstrumentCatalog.Count ||
            manifest.Instruments.Any(x => x is null || !Enum.IsDefined(x.Id)) ||
            manifest.Instruments.Select(x => x.Id).Distinct().Count() != InstrumentCatalog.Count)
            throw new InvalidDataException("音源表には13音色を重複なく指定してください。");
        var result = new DrumSample[InstrumentCatalog.Count];
        long bytes = 0;
        foreach (var entry in manifest.Instruments)
        {
            string name = InstrumentCatalog.Get(entry.Id).Name;
            try
            {
                if (!double.IsFinite(entry.Gain) || entry.Gain <= 0 || entry.Gain > 2)
                    throw new InvalidDataException("gainは0より大きく2以下で指定してください。");
                if (string.IsNullOrWhiteSpace(entry.File) || Path.IsPathRooted(entry.File) ||
                    !string.Equals(Path.GetExtension(entry.File), ".wav", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("キット内のWAV相対パスを指定してください。");
                string path = Path.GetFullPath(Path.Combine(root, entry.File));
                if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("キットフォルダー外の音源は参照できません。");
                // Do not follow junctions or symbolic links out of the authorized kit directory.
                for (string? check = path; check != null; check = Path.GetDirectoryName(check))
                {
                    if ((File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("音源のパスにリンクを使用できません。");
                    if (string.Equals(check, root, StringComparison.OrdinalIgnoreCase)) break;
                }
                var stereo = ReadWav(path);
                if (outputRate != AssetRate) stereo = Resample(stereo, AssetRate, outputRate);
                bytes += stereo.LongLength * sizeof(float);
                if (bytes > 64 * 1024 * 1024) throw new InvalidDataException("変換後PCMが合計64MiBを超えています。");
                result[(int)entry.Id] = new(stereo, (float)entry.Gain);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { throw new InvalidDataException($"{name}: {ex.Message}", ex); }
        }
        return new(manifest.KitId, outputRate, result);
    }

    public static float[] ReadWav(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > 10 * 1024 * 1024 || info.Length < 44) throw new InvalidDataException("WAVの容量またはヘッダーが不正です。");
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        string Tag() => System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (Tag() != "RIFF") throw new InvalidDataException("非圧縮PCM WAVが必要です。");
        long end = reader.ReadUInt32() + 8L;
        if (end != stream.Length || Tag() != "WAVE") throw new InvalidDataException("RIFF長またはWAVEヘッダーが不正です。");
        int channels = 0, rate = 0, align = 0, bits = 0, format = 0;
        uint byteRate = 0;
        long dataOffset = 0;
        int dataBytes = 0;
        bool hasFormat = false, hasData = false;
        while (stream.Position < end)
        {
            if (end - stream.Position < 8) throw new InvalidDataException("WAVチャンクが途中で終了しています。");
            string tag = Tag();
            uint size = reader.ReadUInt32();
            long next = stream.Position + size + (size & 1);
            if (next > end) throw new InvalidDataException("WAVチャンク長が不正です。");
            if (tag == "fmt ")
            {
                if (hasFormat || size < 16) throw new InvalidDataException("fmtチャンクが不正です。");
                hasFormat = true; format = reader.ReadUInt16(); channels = reader.ReadUInt16();
                rate = reader.ReadInt32(); byteRate = reader.ReadUInt32(); align = reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (tag == "data")
            {
                if (hasData) throw new InvalidDataException("dataチャンクが重複しています。");
                hasData = true; dataOffset = stream.Position; dataBytes = checked((int)size);
            }
            stream.Position = next;
        }
        if (!hasFormat || !hasData || format != 1 || channels is not (1 or 2) || rate != AssetRate || bits != 16 ||
            align != channels * 2 || byteRate != rate * align || dataBytes % align != 0)
            throw new InvalidDataException("16bit・44.1kHz・モノラル／ステレオのPCM WAVが必要です。");
        int frames = dataBytes / align;
        if (frames < AssetRate * .02 || frames > AssetRate * 15)
            throw new InvalidDataException("音源の長さは0.02～15秒です。");
        var output = new float[frames * 2];
        stream.Position = dataOffset;
        for (int f = 0; f < frames; f++)
        {
            float left = reader.ReadInt16() / 32768f;
            float right = channels == 2 ? reader.ReadInt16() / 32768f : left;
            output[f * 2] = left; output[f * 2 + 1] = right;
        }
        return output;
    }

    static float[] Resample(float[] input, int fromRate, int toRate)
    {
        int oldFrames = input.Length / 2;
        int frames = checked((int)Math.Round(oldFrames * (double)toRate / fromRate));
        var result = new float[frames * 2];
        for (int f = 0; f < frames; f++)
        {
            double position = f * (double)fromRate / toRate;
            int first = Math.Min((int)position, oldFrames - 1), second = Math.Min(first + 1, oldFrames - 1);
            float part = (float)(position - first);
            for (int c = 0; c < 2; c++) result[f * 2 + c] = input[first * 2 + c] * (1 - part) + input[second * 2 + c] * part;
        }
        return result;
    }
}
