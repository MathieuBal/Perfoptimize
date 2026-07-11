using Perfoptimize.Core.Hardware;
using Perfoptimize.Core.Hardware.Windows;

namespace Perfoptimize.Core.Tests;

public class WindowsParsingTests
{
    [Fact]
    public void DecodeMonitorName_stops_at_trailing_zeros()
    {
        ushort[] encoded = [(ushort)'D', (ushort)'E', (ushort)'L', (ushort)'L', (ushort)' ',
                            (ushort)'S', (ushort)'2', (ushort)'7', (ushort)'2', (ushort)'1',
                            (ushort)'D', (ushort)'G', (ushort)'F', 0, 0, 0];

        Assert.Equal("DELL S2721DGF", WindowsParsing.DecodeMonitorName(encoded));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new ushort[0])]
    [InlineData(new ushort[] { 0, 0 })]
    public void DecodeMonitorName_returns_null_for_empty_input(ushort[]? encoded)
    {
        Assert.Null(WindowsParsing.DecodeMonitorName(encoded));
    }

    [Theory]
    [InlineData(3, StorageMediaType.Hdd)]
    [InlineData(4, StorageMediaType.Ssd)]
    [InlineData(5, StorageMediaType.Scm)]
    [InlineData(0, StorageMediaType.Unknown)]
    [InlineData(99, StorageMediaType.Unknown)]
    public void MapMsftMediaType_matches_documented_values(int raw, StorageMediaType expected)
    {
        Assert.Equal(expected, WindowsParsing.MapMsftMediaType(raw));
    }
}
