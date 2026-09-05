using System.ComponentModel.DataAnnotations;

namespace NextMovie.Api.Infrastructure.Tmdb;

/// <summary>Configuration for the TMDb integration.</summary>
public sealed class TmdbOptions
{
    public const string SectionName = "Tmdb";

    /// <summary>
    /// TMDb v4 API Read Access Token, sent as a bearer token.
    /// </summary>
    /// <remarks>
    /// Never stored in the repository. Supplied locally via .NET user-secrets and
    /// via environment configuration elsewhere. See README.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string ApiReadAccessToken { get; init; } = string.Empty;

    /// <summary>Base address of the TMDb REST API. Trailing slash is required for relative URI resolution.</summary>
    [Required]
    public string BaseUrl { get; init; } = "https://api.themoviedb.org/3/";
}
