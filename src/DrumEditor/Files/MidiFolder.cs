namespace DrumEditor.Files;

public sealed record MidiFolderEntry(string Name, string FullPath);

public static class MidiFolder
{
    public static MidiFolderEntry[] List(string directory) => Directory.EnumerateFiles(directory)
        .Where(file => Path.GetExtension(file).Equals(".mid", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(file).Equals(".midi", StringComparison.OrdinalIgnoreCase))
        .Select(file => new MidiFolderEntry(Path.GetFileName(file), Path.GetFullPath(file)))
        .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static string ExportPath(string directory, string name)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.Length > 200)
            throw new InvalidDataException("ファイル名を200文字以内で入力してください。フォルダーのパスや使用できない文字は含められません。");
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'))
            throw new InvalidDataException("このファイル名はWindowsで使用できません。");
        string extension = Path.GetExtension(name);
        if (extension.Length == 0) name += ".mid";
        else if (!extension.Equals(".mid", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".midi", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("拡張子は.midまたは.midiを指定してください。");
        return Path.Combine(Path.GetFullPath(directory), name);
    }

    public static string SuggestedName(string patternName)
    {
        string name = string.Concat(patternName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        if (name.Length > 180) name = name[..180];
        try { return Path.GetFileName(ExportPath(".", name + ".mid")); }
        catch (InvalidDataException) { return "Pattern.mid"; }
    }
}
