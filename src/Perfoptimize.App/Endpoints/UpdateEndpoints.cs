namespace Perfoptimize.App.Endpoints;

/// <summary>Self-update: check GitHub Releases and, on the user's request, update + restart.</summary>
internal static class UpdateEndpoints
{
    public static void MapUpdateEndpoints(this WebApplication app)
    {
        var service = new UpdateService();

        // Is a newer release available? Nothing is downloaded here.
        app.MapGet("/api/update", async () =>
        {
            var status = await service.CheckAsync();
            return Results.Json(new
            {
                status.Installed,
                status.Available,
                status.CurrentVersion,
                status.NewVersion,
                status.Error,
            });
        });

        // Explicit user action only: download the update and restart into it.
        app.MapPost("/api/update/apply", async () =>
        {
            var (ok, message) = await service.ApplyAsync();
            return Results.Json(new { ok, message });
        });
    }
}
