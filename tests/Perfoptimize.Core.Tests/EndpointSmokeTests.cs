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
