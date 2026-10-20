namespace ChordDetection.Infrastructure.Audio;

/// <summary>
/// Entry point for opening audio files: WAV is read directly, other formats via Media Foundation.
/// </summary>
public static class AudioFileLoader
{
    /// <summary>Extensions offered in the open dialog (Media Foundation formats need Windows).</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } =
        [".wav", ".mp3", ".m4a", ".aac", ".wma", ".flac"];

    /// <summary>Filter string for an open-file dialog.</summary>
    public static string DialogFilter
    {
        get
        {
            var patterns = string.Join(";", SupportedExtensions.Select(e => "*" + e));
            return $"音声ファイル ({patterns})|{patterns}|すべてのファイル (*.*)|*.*";
        }
    }

    public static AudioData Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("ファイルが見つかりません。", path);

        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            return WavFileLoader.Load(path);

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("WAV以外の形式は Windows（Media Foundation）でのみ読み込めます。");

        try
        {
            return MediaFoundationFileLoader.Load(path);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            throw new NotSupportedException(
                $"この形式は読み込めませんでした（{Path.GetExtension(path)}）。Windows に対応コーデックがあるか確認してください。", ex);
        }
    }
}
