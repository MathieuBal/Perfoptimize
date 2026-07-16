using Perfoptimize.Core.Tools;

namespace Perfoptimize.App.Endpoints;

/// <summary>Tooling: PresentMon status and one-click install.</summary>
internal static class ToolsEndpoints
{
    public static void MapToolsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/tools", () =>
        {
            var path = PresentMonInstaller.FindInstalled();
            return Results.Json(new
            {
                presentMon = new { installed = path is not null, path },
                platform = new { isWindows = OperatingSystem.IsWindows() },
            });
        });

        app.MapPost("/api/tools/presentmon", async (IHttpClientFactory factory) =>
        {
            if (!OperatingSystem.IsWindows())
                return Results.Json(new { success = false, message = "PresentMon is a Windows tool." });
            var installer = new PresentMonInstaller(factory.CreateClient());
            var result = await installer.InstallLatestAsync();
            if (result.Success && result.ExecutablePath is not null)
                Environment.SetEnvironmentVariable("PERFOPTIMIZE_PRESENTMON", result.ExecutablePath);
            return Results.Json(new { result.Success, result.Message, result.ExecutablePath, result.Version });
        });
    }
}
