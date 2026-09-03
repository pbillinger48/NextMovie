using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using NextMovie.Api.Features.Health;

namespace NextMovie.Api.Tests.Features.Health;

/// <summary>
/// Smoke test for the health endpoint.
/// </summary>
/// <remarks>
/// This drives the real host rather than calling the handler directly. Calling
/// <c>GetHealth.Handle()</c> in isolation would assert almost nothing — it
/// returns a constant. The value here is proving the application boots, builds
/// its service container, and routes a request end to end, which is exactly what
/// breaks first when dependency injection or middleware is misconfigured.
/// </remarks>
public sealed class GetHealthTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public GetHealthTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Returns_ok_with_healthy_status()
    {
        using var client = _factory.CreateClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var response = await client.GetAsync("/api/v1/health", cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>(cts.Token);

        Assert.NotNull(body);
        Assert.Equal("healthy", body.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
    }
}
