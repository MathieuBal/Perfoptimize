using System.Diagnostics;

namespace Perfoptimize.App;

/// <summary>Opens the user's default browser at the app's local URL.</summary>
internal static class BrowserLauncher
{
    public static void Open(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // UseShellExecute lets the OS resolve the default browser.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // Headless or no browser: the URL was already printed to the console.
        }
    }
}
