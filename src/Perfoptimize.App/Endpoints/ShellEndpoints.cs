using System.Reflection;

namespace Perfoptimize.App.Endpoints;

/// <summary>The app shell (embedded UI assets), version, and clean shutdown.</summary>
internal static class ShellEndpoints
{
    public static void MapShellEndpoints(this WebApplication app)
    {
        // UI (embedded assets, single-file safe).
        app.MapGet("/", () => Results.Content(WebAssets.Read("index.html"), "text/html; charset=utf-8"));
        app.MapGet("/app.js", () => Results.Content(WebAssets.Read("app.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/session-fidelity.js", () => Results.Content(WebAssets.Read("session-fidelity.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/alpha-feedback.js", () => Results.Content(WebAssets.Read("alpha-feedback.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/styles.css", () => Results.Content(WebAssets.Read("styles.css"), "text/css; charset=utf-8"));

        app.MapGet("/api/version", () => Results.Json(new
        {
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev",
        }));

        app.MapPost("/api/shutdown", (IHostApplicationLifetime life) =>
        {
            // Fire after the response is sent so the browser sees the goodbye.
            _ = Task.Run(async () => { await Task.Delay(300); life.StopApplication(); });
            return Results.Ok();
        });
    }
}
