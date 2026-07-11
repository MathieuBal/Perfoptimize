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
    /// <summary>Detects the encoding of an existing file from its BOM (UTF-8 without BOM otherwise).</summary>
    public static Encoding DetectEncoding(string path)
    {
        Span<byte> preamble = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        var read = stream.Read(preamble);

        if (read >= 3 && preamble[0] == 0xEF && preamble[1] == 0xBB && preamble[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        if (read >= 2 && preamble[0] == 0xFF && preamble[1] == 0xFE)
            return Encoding.Unicode;           // UTF-16 LE
        if (read >= 2 && preamble[0] == 0xFE && preamble[1] == 0xFF)
            return Encoding.BigEndianUnicode;  // UTF-16 BE
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
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
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.perfoptimize-tmp");

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
