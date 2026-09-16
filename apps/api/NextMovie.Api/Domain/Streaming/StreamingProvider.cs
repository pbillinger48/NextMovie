namespace NextMovie.Api.Domain.Streaming;

/// <summary>
/// A service a film can be watched on.
/// </summary>
/// <remarks>
/// Reference data, keyed on TMDb's provider id for the same reason
/// <see cref="Genre"/> is: the identifier is stable, shared, and already the one
/// every response speaks in. Rows arrive as availability is fetched rather than
/// from a seed — the set of services differs per region and changes, and a seeded
/// list would be wrong somewhere from the day it was written.
/// </remarks>
public class StreamingProvider
{
    /// <summary>TMDb's provider identifier.</summary>
    public required int Id { get; init; }

    public required string Name { get; set; }

    /// <summary>Relative TMDb logo path. Combine with an image base URL to render.</summary>
    public string? LogoPath { get; set; }
}
