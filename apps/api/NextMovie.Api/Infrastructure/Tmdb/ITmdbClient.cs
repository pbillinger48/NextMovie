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

    /// <summary>
    /// Films TMDb considers related to this one.
    /// </summary>
    /// <remarks>
    /// The candidate source for recommendations (ADR-0008). TMDb computes this
    /// from a user base orders of magnitude larger than ours, which is not
    /// something we can outcompute from nineteen genre labels.
    /// </remarks>
    /// <param name="tmdbId">The film to find relatives of.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TmdbException">TMDb was unreachable or returned a failure.</exception>
    Task<TmdbSearchResponse> GetRelatedMoviesAsync(int tmdbId, CancellationToken cancellationToken);

    /// <summary>
    /// The best-reviewed films in a genre.
    /// </summary>
    /// <remarks>
    /// The second candidate source. Relatedness answers "what else is like the
    /// films you loved", which for somebody who has seen eight hundred films
    /// returns mostly films they have already seen. This answers "what are the
    /// best films of this kind" — which is the question a recommendation is
    /// actually for.
    /// </remarks>
    /// <param name="genreId">TMDb genre identifier.</param>
    /// <param name="minimumVotes">Votes a film needs before its rating is believed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TmdbException">TMDb was unreachable or returned a failure.</exception>
    Task<TmdbSearchResponse> DiscoverBestInGenreAsync(
        int genreId,
        int minimumVotes,
        CancellationToken cancellationToken);

    /// <summary>Where a film can be watched, by country.</summary>
    /// <param name="tmdbId">The film.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="TmdbException">TMDb was unreachable or returned a failure.</exception>
    Task<TmdbWatchProvidersResponse> GetWatchProvidersAsync(int tmdbId, CancellationToken cancellationToken);
}
