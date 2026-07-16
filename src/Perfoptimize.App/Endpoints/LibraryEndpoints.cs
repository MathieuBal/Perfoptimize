using Perfoptimize.Core.Library;

namespace Perfoptimize.App.Endpoints;

/// <summary>Discover installed games and read their config files (bounded to discovered games).</summary>
internal static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this WebApplication app)
    {
        app.MapGet("/api/library", () =>
        {
            var games = new InstalledGameScanner().Scan();
            return Results.Json(new
            {
                games = games.Select(g => new { g.Name, g.InstallDir, source = g.Source.ToString(), g.AppId }),
            });
        });

        app.MapGet("/api/library/config", (string dir) =>
        {
            // Only read config files of a game we actually discovered installed —
            // never an arbitrary path handed to us by the client.
            var game = new InstalledGameScanner().Scan()
                .FirstOrDefault(g => string.Equals(g.InstallDir, dir, StringComparison.OrdinalIgnoreCase));
            if (game is null)
                return Results.Json(new { ok = false, message = "Jeu non trouvé dans la bibliothèque." });

            var configs = new GameConfigScanner().FindConfigs(game.Name, game.InstallDir);
            return Results.Json(new
            {
                ok = true,
                game = game.Name,
                configs = configs.Select(c => new
                {
                    c.Path, c.Format, c.TotalKeys,
                    settings = c.GraphicsSettings.Select(s => new { s.Section, s.Key, s.Value }),
                }),
            });
        });
    }
}
