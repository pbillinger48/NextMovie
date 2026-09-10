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

    /// <summary>Fetches the full details TMDb holds for one film.</summary>
    /// <param name="tmdbId">TMDb's identifier for the film.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TmdbException">
    /// TMDb was unreachable, or returned a failure — including 404 for a film it
    /// no longer carries, which callers may want to treat differently from an
    /// outage.
    /// </exception>
    Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken);
}
