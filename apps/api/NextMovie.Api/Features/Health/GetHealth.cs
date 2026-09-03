using System.Reflection;
using Microsoft.AspNetCore.Http.HttpResults;

namespace NextMovie.Api.Features.Health;

/// <summary>
/// Liveness probe used by monitoring and container orchestration.
/// </summary>
/// <remarks>
/// This deliberately touches no database and no external service. It answers
/// "is this process up and serving?", which is the question an orchestrator
/// restarts a container over. Checking dependencies here would mean a brief
/// TMDb outage could get healthy instances killed and restarted in a loop.
/// Dependency checks belong on a separate readiness endpoint, added once there
/// are dependencies worth gating traffic on.
/// </remarks>
public static class GetHealth
{
    private static readonly string BuildVersion =
        typeof(GetHealth).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    /// <summary>Registers the health endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/health", Handle)
            .WithName(nameof(GetHealth))
            .WithSummary("Liveness probe")
            .WithDescription("Reports whether the API process is running and serving requests.");

        return app;
    }

    // TypedResults rather than Results.Ok: the concrete return type gives the
    // OpenAPI document the response schema at compile time, which is what
    // ADR-0002 generates the TypeScript client from.
    private static Ok<HealthResponse> Handle() =>
        TypedResults.Ok(new HealthResponse(
            Status: "healthy",
            Version: BuildVersion,
            TimestampUtc: DateTimeOffset.UtcNow));
}

/// <summary>Response body returned by the health endpoint.</summary>
/// <param name="Status">Always <c>healthy</c> when the process is serving requests.</param>
/// <param name="Version">Informational assembly version of the running build.</param>
/// <param name="TimestampUtc">Server time at which the probe was answered.</param>
public sealed record HealthResponse(string Status, string Version, DateTimeOffset TimestampUtc);
