namespace Perfoptimize.Core.Network;

/// <summary>
/// Assigns a conservative trust level to a connection. It never claims a
/// connection is "safe" — it only distinguishes an identified program from an
/// unidentified one and raises a flag on the one strong signal that is cheap and
/// reliable: an executable running from a temp/downloads folder, a classic
/// malware trait. Pure and unit-tested.
/// </summary>
public static class ConnectionClassifier
{
    private static readonly string[] TempMarkers =
    [
        @"\temp\", @"\tmp\", @"\downloads\", @"\appdata\local\temp",
        "/tmp/", "/var/tmp/",
    ];

    public static (ConnectionTrust Trust, string? Note) Classify(
        string? processName, string? processPath, string remoteAddress, int remotePort, string state)
    {
        if (!string.IsNullOrEmpty(processPath))
        {
            var lower = processPath.ToLowerInvariant();
            if (TempMarkers.Any(marker => lower.Contains(marker)))
                return (ConnectionTrust.Suspicious,
                    "L'exécutable tourne depuis un dossier temporaire / téléchargements — inhabituel pour un vrai logiciel, à vérifier.");
        }

        if (string.IsNullOrWhiteSpace(processName))
            return (ConnectionTrust.Unknown,
                "Programme propriétaire non identifié — regarde de plus près si tu ne l'attends pas.");

        return (ConnectionTrust.Normal, null);
    }
}
