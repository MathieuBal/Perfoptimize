using System.Xml.Linq;

namespace Perfoptimize.Core.Startup;

/// <summary>The interesting bits of a Task Scheduler task definition.</summary>
public sealed record ScheduledTaskInfo(string? Author, string? Command, string? Arguments);

/// <summary>
/// Parses a Task Scheduler XML definition (as found under
/// %WINDIR%\System32\Tasks) into its author and the program it launches. Only
/// "Exec" actions carry a command line; COM-handler tasks return null. Pure and
/// unit-tested — the scanner only reads the files.
/// </summary>
public static class ScheduledTaskParser
{
    public static ScheduledTaskInfo? Parse(string xml)
    {
        try
        {
            var document = XDocument.Parse(xml);
            var root = document.Root;
            if (root is null)
                return null;

            XNamespace ns = root.Name.Namespace;
            var author = root.Element(ns + "RegistrationInfo")?.Element(ns + "Author")?.Value?.Trim();
            var exec = root.Element(ns + "Actions")?.Element(ns + "Exec");
            var command = exec?.Element(ns + "Command")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(command))
                return null; // no Exec action (e.g. a COM handler) — nothing to attribute

            var arguments = exec?.Element(ns + "Arguments")?.Value?.Trim();
            return new ScheduledTaskInfo(
                string.IsNullOrWhiteSpace(author) ? null : author,
                command,
                string.IsNullOrWhiteSpace(arguments) ? null : arguments);
        }
        catch
        {
            return null;
        }
    }
}
