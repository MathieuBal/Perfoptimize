using System.Text.RegularExpressions;

namespace Perfoptimize.Core.Hardware.Linux;

/// <summary>
/// Pure parsers for the Linux virtual files and tool outputs used by
/// <see cref="LinuxHardwareDetector"/>. They take raw text so they can be
/// unit-tested with captured fixtures.
/// </summary>
public static partial class LinuxProcParser
{
    /// <summary>Parses /proc/cpuinfo.</summary>
    public static CpuInfo ParseCpuInfo(string cpuInfoText)
    {
        var name = "Unknown";
        var vendor = "Unknown";
        var logical = 0;
        var coreIds = new HashSet<(string PhysicalId, string CoreId)>();
        string physicalId = "0", coreId = "";
        int? coresPerSocket = null;

        foreach (var line in cpuInfoText.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
                continue;
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            switch (key)
            {
                case "processor":
                    logical++;
                    break;
                case "model name" when name == "Unknown":
                    name = value;
                    break;
                case "vendor_id" when vendor == "Unknown":
                    vendor = value;
                    break;
                case "physical id":
                    physicalId = value;
                    break;
                case "cpu cores" when coresPerSocket is null && int.TryParse(value, out var cores):
                    coresPerSocket = cores;
                    break;
                case "core id":
                    coreId = value;
                    coreIds.Add((physicalId, coreId));
                    break;
            }
        }

        var physical = coreIds.Count > 0 ? coreIds.Count : coresPerSocket ?? logical;
        return new CpuInfo
        {
            Name = name,
            Vendor = vendor switch
            {
                "GenuineIntel" => "Intel",
                "AuthenticAMD" => "AMD",
                _ => vendor,
            },
            PhysicalCores = physical,
            LogicalCores = logical,
        };
    }

    /// <summary>Parses /proc/meminfo. Values are reported by the kernel in kB.</summary>
    public static MemoryInfo ParseMemInfo(string memInfoText)
    {
        long totalKb = 0;
        long? availableKb = null;
        foreach (var line in memInfoText.Split('\n'))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                totalKb = ExtractKb(line);
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                availableKb = ExtractKb(line);
        }
        return new MemoryInfo
        {
            TotalBytes = totalKb * 1024,
            AvailableBytes = availableKb * 1024,
        };

        static long ExtractKb(string line)
        {
            var digits = new string(line.Where(char.IsDigit).ToArray());
            return long.TryParse(digits, out var kb) ? kb : 0;
        }
    }

    /// <summary>
    /// Parses `lspci -mm` output and keeps display controllers, e.g.:
    /// 01:00.0 "VGA compatible controller" "NVIDIA Corporation" "GA106 [GeForce RTX 3060]" -ra1 "" ""
    /// </summary>
    public static IReadOnlyList<GpuInfo> ParseLspci(string lspciText)
    {
        var gpus = new List<GpuInfo>();
        foreach (var line in lspciText.Split('\n'))
        {
            var fields = QuotedFieldRegex().Matches(line).Select(m => m.Groups[1].Value).ToList();
            if (fields.Count < 3)
                continue;
            var deviceClass = fields[0];
            if (!deviceClass.Contains("VGA", StringComparison.OrdinalIgnoreCase) &&
                !deviceClass.Contains("3D", StringComparison.OrdinalIgnoreCase) &&
                !deviceClass.Contains("Display", StringComparison.OrdinalIgnoreCase))
                continue;

            var rawVendor = fields[1];
            var rawDevice = fields[2];
            // Prefer the marketing name in brackets: "GA106 [GeForce RTX 3060]" → "GeForce RTX 3060".
            var bracket = Regex.Match(rawDevice, @"\[(?<name>[^\]]+)\]");
            var name = bracket.Success ? bracket.Groups["name"].Value : rawDevice;

            var vendor = GpuClassifier.NormalizeVendor(rawVendor, name);
            gpus.Add(new GpuInfo
            {
                Name = name,
                Vendor = vendor,
                IsDiscrete = GpuClassifier.IsDiscrete(name, vendor),
            });
        }
        return gpus;
    }

    /// <summary>Parses /etc/os-release for the distribution name and version.</summary>
    public static OperatingSystemInfo ParseOsRelease(string osReleaseText, string architecture)
    {
        string? prettyName = null, versionId = null;
        foreach (var line in osReleaseText.Split('\n'))
        {
            if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                prettyName = line["PRETTY_NAME=".Length..].Trim().Trim('"');
            else if (line.StartsWith("VERSION_ID=", StringComparison.Ordinal))
                versionId = line["VERSION_ID=".Length..].Trim().Trim('"');
        }
        return new OperatingSystemInfo
        {
            Name = prettyName ?? "Linux",
            Version = versionId ?? "Unknown",
            Architecture = architecture,
        };
    }

    /// <summary>
    /// Parses `xrandr --query` output. A connected output looks like:
    /// "DP-1 connected primary 2560x1440+0+0 ..." followed by mode lines where
    /// the current refresh rate is marked with '*' (e.g. "143.91*+").
    /// </summary>
    public static IReadOnlyList<DisplayInfo> ParseXrandr(string xrandrText)
    {
        var displays = new List<DisplayInfo>();
        string? currentName = null;
        var currentPrimary = false;
        int currentWidth = 0, currentHeight = 0;
        int? currentRefresh = null;

        void Flush()
        {
            if (currentName is not null && currentWidth > 0 && currentHeight > 0)
            {
                displays.Add(new DisplayInfo
                {
                    Name = currentName,
                    WidthPixels = currentWidth,
                    HeightPixels = currentHeight,
                    RefreshRateHz = currentRefresh,
                    IsPrimary = currentPrimary,
                });
            }
            currentName = null;
        }

        foreach (var line in xrandrText.Split('\n'))
        {
            var connected = ConnectedOutputRegex().Match(line);
            if (connected.Success)
            {
                Flush();
                currentName = connected.Groups["name"].Value;
                currentPrimary = connected.Groups["primary"].Success;
                currentWidth = int.Parse(connected.Groups["w"].Value);
                currentHeight = int.Parse(connected.Groups["h"].Value);
                currentRefresh = null;
                continue;
            }

            if (currentName is not null && currentRefresh is null)
            {
                var active = ActiveRefreshRegex().Match(line);
                if (active.Success)
                    currentRefresh = (int)Math.Round(double.Parse(
                        active.Groups["rate"].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        Flush();
        return displays;
    }

    [GeneratedRegex("\"([^\"]*)\"")]
    private static partial Regex QuotedFieldRegex();

    [GeneratedRegex(@"^(?<name>\S+) connected (?<primary>primary )?(?<w>\d+)x(?<h>\d+)\+\d+\+\d+")]
    private static partial Regex ConnectedOutputRegex();

    [GeneratedRegex(@"(?<rate>\d+(?:\.\d+)?)\*")]
    private static partial Regex ActiveRefreshRegex();
}
