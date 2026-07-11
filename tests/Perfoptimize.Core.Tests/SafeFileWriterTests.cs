using System.Text;
using Perfoptimize.Core.Configs;

namespace Perfoptimize.Core.Tests;

public class SafeFileWriterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"perfoptimize-safewrite-{Guid.NewGuid():N}");

    public SafeFileWriterTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathFor(string name) => Path.Combine(_directory, name);

    [Fact]
    public void Utf8_bom_is_preserved()
    {
        var path = PathFor("bom.ini");
        File.WriteAllText(path, "[A]\nx=1", new UTF8Encoding(true));

        SafeFileWriter.ReplaceAtomically(path, "[A]\nx=2");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Equal("[A]\nx=2", File.ReadAllText(path));
    }

    [Fact]
    public void Utf16_files_stay_utf16()
    {
        var path = PathFor("utf16.ini");
        File.WriteAllText(path, "[A]\nx=1", Encoding.Unicode);

        SafeFileWriter.ReplaceAtomically(path, "[A]\nx=2");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xFF, 0xFE], bytes.Take(2)); // UTF-16 LE BOM intact
        Assert.Equal("[A]\nx=2", File.ReadAllText(path, Encoding.Unicode));
    }

    [Fact]
    public void Plain_utf8_gets_no_spurious_bom()
    {
        var path = PathFor("plain.ini");
        File.WriteAllText(path, "[A]\nx=1", new UTF8Encoding(false));

        SafeFileWriter.ReplaceAtomically(path, "[A]\nx=2");

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("[A]\nx=2", File.ReadAllText(path));
    }

    [Fact]
    public void No_temp_file_is_left_behind()
    {
        var path = PathFor("clean.ini");
        File.WriteAllText(path, "x=1");

        SafeFileWriter.ReplaceAtomically(path, "x=2");

        Assert.Single(Directory.GetFiles(_directory));
    }
}
