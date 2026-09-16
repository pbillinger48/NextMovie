namespace NextMovie.Api.Domain.Streaming;

/// <summary>
/// Which services exist in a country.
/// </summary>
/// <remarks>
/// The companion to <see cref="IAvailabilityProvider"/>, and separate from it for
/// the same reason ADR-0010 gave for that one: the source of this data is
/// replaceable. TMDb answers it today; a paid availability service would answer
/// it differently and better, and only the implementation should have to change.
/// </remarks>
internal interface IServiceDirectory
{
    /// <summary>Every service offered in one country.</summary>
    /// <returns>
    /// <see langword="null"/> when the source could not be asked, which is not
    /// the same as a country with no services. The distinction matters: the first
    /// means keep what we have, the second means the list is genuinely empty.
    /// </returns>
    Task<IReadOnlyList<RegionalService>?> GetServicesAsync(string region, CancellationToken cancellationToken);
}

/// <summary>One service on offer in a country.</summary>
/// <param name="ProviderId">The service's identifier, shared with offers on films.</param>
/// <param name="Name">Name to show.</param>
/// <param name="LogoPath">Relative logo path, when the source has one.</param>
/// <param name="DisplayPriority">Ordering hint for this country — lower is more prominent.</param>
internal sealed record RegionalService(int ProviderId, string Name, string? LogoPath, int DisplayPriority);
