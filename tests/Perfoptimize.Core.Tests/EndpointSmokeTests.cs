using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Perfoptimize.Core.Tests;

/// <summary>
/// End-to-end smoke tests: boot the real app in-memory and check that every
/// read endpoint actually responds. This is what covers the I/O plumbing the
/// pure unit tests can't — the endpoints, JSON serialization and the embedded
/// web assets all being wired together. The continuous background sampler is
/// removed so the tests exercise the endpoints, not a live capture loop.
/// </summary>
public class EndpointSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EndpointSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>()));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/app.js")]
    [InlineData("/session-fidelity.js")]
    [InlineData("/alpha-feedback.js")]
    [InlineData("/styles.css")]
    [InlineData("/api/version")]
    [InlineData("/api/hardware")]
    [InlineData("/api/games")]
    [InlineData("/api/system")]
    [InlineData("/api/library")]
    [InlineData("/api/network")]
    [InlineData("/api/incidents")]
    [InlineData("/api/tools")]
    [InlineData("/api/settings-guide")]
    public async Task Endpoint_responds_successfully(string url)
    {
        var client = _factory.CreateClient();
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cockpit_shell_is_served()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");
        Assert.Contains("PERFOPTIMIZE", html);
    }

    [Fact]
    public async Task Session_fidelity_binding_loads_after_the_main_app_and_sends_the_analysis_id()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");
        var binding = await client.GetStringAsync("/session-fidelity.js");

        var appIndex = html.IndexOf("/app.js", StringComparison.Ordinal);
        var bindingIndex = html.IndexOf("/session-fidelity.js", StringComparison.Ordinal);
        Assert.True(appIndex >= 0 && bindingIndex > appIndex,
            "session-fidelity.js must load after app.js so it can extend applyContext");
        Assert.Contains("state.lastReport", binding);
        Assert.Contains("sessionId", binding);
        Assert.Contains("baseApplyContext", binding);
    }

    [Fact]
    public async Task Session_fidelity_binding_uses_exact_backups_after_a_real_apply()
    {
        var binding = await _factory.CreateClient().GetStringAsync("/session-fidelity.js");

        Assert.Contains("state.applyPhase === \"applied\"", binding);
        Assert.Contains("state.applyResult.backups", binding);
        Assert.Contains("backups,", binding);
        Assert.Contains("exact,", binding);
    }

    [Fact]
    public async Task Alpha_report_binding_loads_last_and_uses_the_exact_session_id()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/");
        var binding = await client.GetStringAsync("/alpha-feedback.js");

        var fidelityIndex = html.IndexOf("/session-fidelity.js", StringComparison.Ordinal);
        var reportIndex = html.IndexOf("/alpha-feedback.js", StringComparison.Ordinal);
        Assert.True(fidelityIndex >= 0 && reportIndex > fidelityIndex,
            "alpha-feedback.js must load after session fidelity is established");
        Assert.Contains("state.lastReport.sessionId", binding);
        Assert.Contains("/api/export-report?sessionId=", binding);
    }

    [Fact]
    public async Task Alpha_report_refuses_a_missing_session_id()
    {
        using var response = await _factory.CreateClient().GetAsync("/api/export-report");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Settings_guide_tailors_the_renderer_to_the_bottleneck()
    {
        var json = await _factory.CreateClient().GetStringAsync("/api/settings-guide?bottleneck=Cpu");
        Assert.Contains("dx12", json);
        Assert.Contains("recommendation", json);
    }

    [Fact]
    public async Task Incidents_endpoint_is_shaped_and_safe_off_windows()
    {
        var json = await _factory.CreateClient().GetStringAsync("/api/incidents");
        Assert.Contains("incidents", json);
    }
}
