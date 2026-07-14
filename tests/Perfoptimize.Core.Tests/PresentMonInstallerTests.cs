using Perfoptimize.Core.Tools;

namespace Perfoptimize.Core.Tests;

public class PresentMonInstallerTests
{
    [Fact]
    public void SelectConsoleAsset_prefers_the_x64_console_exe()
    {
        ReleaseAsset[] assets =
        [
            new("PresentMon-2.3.0-x64.exe", "https://example/exe"),
            new("PresentMon-2.3.0-x86.exe", "https://example/x86"),
            new("PresentMonOverlay-2.3.0-x64.exe", "https://example/overlay"),
            new("PresentMonService.exe", "https://example/service"),
            new("Source code (zip)", "https://example/src"),
        ];

        var chosen = PresentMonInstaller.SelectConsoleAsset(assets);

        Assert.NotNull(chosen);
        Assert.Equal("PresentMon-2.3.0-x64.exe", chosen.Value.Name);
    }

    [Fact]
    public void SelectConsoleAsset_never_picks_the_overlay_or_service_builds()
    {
        ReleaseAsset[] assets =
        [
            new("PresentMonOverlay-x64.exe", "https://example/overlay"),
            new("PresentMonService.exe", "https://example/service"),
        ];

        Assert.Null(PresentMonInstaller.SelectConsoleAsset(assets));
    }

    [Fact]
    public void SelectConsoleAsset_falls_back_to_a_zip_when_no_exe_is_offered()
    {
        ReleaseAsset[] assets =
        [
            new("PresentMon-2.3.0-x64.zip", "https://example/zip"),
        ];

        var chosen = PresentMonInstaller.SelectConsoleAsset(assets);

        Assert.NotNull(chosen);
        Assert.EndsWith(".zip", chosen.Value.Name);
    }

    [Fact]
    public void ParseRelease_extracts_the_tag_and_download_assets()
    {
        const string json = """
            {
              "tag_name": "v2.3.0",
              "assets": [
                { "name": "PresentMon-2.3.0-x64.exe", "browser_download_url": "https://example/exe" },
                { "name": "checksums.txt", "browser_download_url": "https://example/sum" }
              ]
            }
            """;

        var (tag, assets) = PresentMonInstaller.ParseRelease(json);

        Assert.Equal("v2.3.0", tag);
        Assert.Equal(2, assets.Count);
        Assert.Equal("https://example/exe", assets[0].DownloadUrl);
    }

    [Fact]
    public void ParseRelease_of_garbage_yields_nothing_rather_than_throwing()
    {
        var (tag, assets) = PresentMonInstaller.ParseRelease("{ not json");

        Assert.Null(tag);
        Assert.Empty(assets);
    }

    [Fact]
    public void SelectConsoleAsset_of_empty_release_returns_null()
    {
        Assert.Null(PresentMonInstaller.SelectConsoleAsset([]));
    }
}
