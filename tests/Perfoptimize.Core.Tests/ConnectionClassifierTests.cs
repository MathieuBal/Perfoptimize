using Perfoptimize.Core.Network;

namespace Perfoptimize.Core.Tests;

public class ConnectionClassifierTests
{
    [Fact]
    public void An_identified_process_is_normal()
    {
        var (trust, note) = ConnectionClassifier.Classify(
            "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "140.82.121.4", 443, "ESTABLISHED");

        Assert.Equal(ConnectionTrust.Normal, trust);
        Assert.Null(note);
    }

    [Fact]
    public void An_unnamed_process_is_unknown()
    {
        var (trust, _) = ConnectionClassifier.Classify(null, null, "45.9.148.1", 4444, "ESTABLISHED");
        Assert.Equal(ConnectionTrust.Unknown, trust);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\Temp\svch0st.exe")]
    [InlineData(@"C:\Users\me\Downloads\crack.exe")]
    [InlineData("/tmp/miner")]
    public void An_executable_running_from_a_temp_folder_is_suspicious(string path)
    {
        var (trust, note) = ConnectionClassifier.Classify("x", path, "45.9.148.1", 4444, "ESTABLISHED");

        Assert.Equal(ConnectionTrust.Suspicious, trust);
        Assert.False(string.IsNullOrWhiteSpace(note));
    }
}
