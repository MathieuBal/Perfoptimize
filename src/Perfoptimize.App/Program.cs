using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Perfoptimize.App;
using Perfoptimize.App.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); // a desktop tool should not spam the console
// Ephemeral loopback port: no conflict with anything the user already runs.
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddHttpClient();
// Continuous background monitoring: sample the machine server-side, always.
builder.Services.AddSingleton<LiveState>();
builder.Services.AddHostedService<LiveMonitorHostedService>();

var app = builder.Build();

// Endpoints are grouped by concern in Endpoints/*.cs (see each MapXxx method).
app.MapShellEndpoints();          // UI assets, version, shutdown
app.MapHardwareEndpoints();       // hardware, games, system advice
app.MapLibraryEndpoints();        // installed games + their configs
app.MapMonitorEndpoints();        // measure/diagnose + settings guide
app.MapLiveDiagnosticsEndpoints();// live monitor, network, incidents
app.MapToolsEndpoints();          // PresentMon status + install
app.MapOptimizationEndpoints();   // preview / apply / restore config

// ----- Launch: print the URL and open the browser ----------------------------
app.Lifetime.ApplicationStarted.Register(() =>
{
    var address = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault() ?? "http://127.0.0.1:5000";
    Console.WriteLine($"Perfoptimize is running at {address}");
    Console.WriteLine("Keep this window open while you use the app. Close it (or use Quit in the UI) to stop.");
    BrowserLauncher.Open(address);
});

app.Run();

/// <summary>Exposes the top-level Program to the test host (WebApplicationFactory).</summary>
public partial class Program;
