using System.Text;

namespace Perfoptimize.Core.Configs;

/// <summary>
/// Atomic, encoding-preserving file replacement for game config files.
/// Games are picky readers: a UTF-16 file rewritten as UTF-8, a stripped BOM
/// or a half-written file can all reset a game's settings. The new content is
/// written to a sibling temp file first, then swapped in atomically.
/// </summary>
public static class SafeFileWriter
{
    /// <summary>
    /// Detects the encoding of an existing file: BOM first, then a strict UTF-8
    /// validation pass. A BOM-less file that is not valid UTF-8 (a legacy
    /// Windows-1252 INI, for instance) falls back to Latin-1, which round-trips
    /// every byte unchanged instead of corrupting accented characters.
    /// </summary>
    public static Encoding DetectEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;           // UTF-16 LE
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;  // UTF-16 BE

        try
        {
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }

    /// <summary>
    /// Replaces the file's content atomically, keeping its original encoding
    /// and BOM. The target is never left half-written: either the old content
    /// survives or the new content is fully in place.
    /// </summary>
    public static void ReplaceAtomically(string path, string newContent)
    {
        var encoding = DetectEncoding(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        // Unique temp name: two Perfoptimize instances must never collide.
        var tempPath = Path.Combine(directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.perfoptimize-tmp");

        try
        {
            File.WriteAllText(tempPath, newContent, encoding);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
