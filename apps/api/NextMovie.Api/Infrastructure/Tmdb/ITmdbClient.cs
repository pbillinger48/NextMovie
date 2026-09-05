using NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Infrastructure.Tmdb;

/// <summary>Read access to TMDb.</summary>
internal interface ITmdbClient
{
    /// <summary>Searches TMDb for films matching a title.</summary>
    /// <param name="title">Free-text title query.</param>
    /// <param name="page">1-based page number. TMDb serves at most 500 pages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TmdbException">TMDb was unreachable or returned a failure.</exception>
    Task<TmdbSearchResponse> SearchMoviesAsync(string title, int page, CancellationToken cancellationToken);
}
