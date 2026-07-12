namespace Perfoptimize.Core.Configs;

/// <summary>
/// Reverts ONE tested key to its original value without touching the rest of
/// the file. A full-backup copy would also wipe every change the game itself
/// wrote in the meantime (resolution picked in the menu, key bindings, ...) —
/// the experiment only owns the key it modified.
/// </summary>
public static class SurgicalRestore
{
    /// <summary>
    /// Sets [<paramref name="section"/>] <paramref name="key"/> back to
    /// <paramref name="originalValue"/>, preserving the file's encoding and
    /// everything else in it. Returns false when the file is unreadable —
    /// callers then fall back to the full backup copy.
    /// </summary>
    public static bool TryRestoreKey(string path, string section, string key, string originalValue)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            var text = File.ReadAllText(path, SafeFileWriter.DetectEncoding(path));
            var restored = IniFile.SetValue(text, section, key, originalValue);
            SafeFileWriter.ReplaceAtomically(path, restored);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
