using System.Globalization;
using NextMovie.Api.Domain;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Infrastructure.Tmdb;

/// <summary>
/// A film mapped out of TMDb's wire format, together with the TMDb genre ids it
/// claims. Genre ids stay separate because resolving them to <see cref="Genre"/>
/// rows requires the database, and keeping that out of the mapper is what makes
/// the mapper a pure, cheaply testable function.
/// </summary>
internal sealed record MappedMovie(Movie Movie, IReadOnlyList<int> GenreIds);

/// <summary>
/// Translates TMDb's representation of a film into ours.
/// </summary>
/// <remarks>
/// This is the anti-corruption boundary. Everything TMDb does that we disagree
/// with gets corrected here, once, rather than leaking into the domain:
/// empty-string dates, zero ratings that mean "unrated", and empty strings where
/// null is meant.
/// </remarks>
internal static class TmdbMovieMapper
{
    /// <summary>
    /// Maps a TMDb search result to a domain <see cref="Movie"/>, or returns
    /// <see langword="null"/> if the record is unusable.
    /// </summary>
    /// <remarks>
    /// Returning null rather than throwing is deliberate: one malformed entry in
    /// a page of twenty should cost us that entry, not the whole search. Callers
    /// are expected to log and skip.
    /// </remarks>
    public static MappedMovie? ToDomain(TmdbMovieDto dto)
    {
        // A film with no TMDb id cannot be deduplicated or refreshed, and one
        // with no title cannot be displayed. Neither is worth persisting.
        if (dto.Id <= 0 || string.IsNullOrWhiteSpace(dto.Title))
        {
            return null;
        }

        var movie = new Movie
        {
            TmdbId = dto.Id,
            Title = dto.Title.Trim(),
            OriginalTitle = Normalize(dto.OriginalTitle),
            Overview = Normalize(dto.Overview),
            PosterPath = Normalize(dto.PosterPath),
            BackdropPath = Normalize(dto.BackdropPath),
            ReleaseDate = ParseReleaseDate(dto.ReleaseDate),
            AverageRating = ParseRating(dto.VoteAverage, dto.VoteCount),
            Popularity = dto.Popularity,
            Language = Normalize(dto.OriginalLanguage),

            // Neither runtime nor status appears in TMDb search results; both are
            // details-endpoint fields. They stay null until a film is enriched,
            // rather than being silently defaulted to a wrong value.
            Runtime = null,
            Status = null,
        };

        return new MappedMovie(movie, dto.GenreIds ?? []);
    }

    /// <summary>Collapses empty and whitespace-only strings to null.</summary>
    /// <remarks>
    /// TMDb uses <c>""</c> where null is meant. Storing empty strings would make
    /// every consumer check both, forever.
    /// </remarks>
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Parses TMDb's release date, tolerating its empty-string-for-unknown convention.</summary>
    private static DateOnly? ParseReleaseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Invariant culture and an exact format: TMDb always sends ISO 8601
        // dates, and parsing them under the host's culture would misread them on
        // a machine with different conventions.
        return DateOnly.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Maps TMDb's community rating, treating an unvoted film as unrated.
    /// </summary>
    /// <remarks>
    /// TMDb reports <c>vote_average: 0</c> for films nobody has rated. Storing
    /// that as a real 0.0 would tell the recommendation engine these are the
    /// worst films ever made, when in fact we know nothing about them. Absence of
    /// data must stay absent.
    /// </remarks>
    private static double? ParseRating(double? voteAverage, int voteCount)
    {
        if (voteCount <= 0 || voteAverage is null or <= 0)
        {
            return null;
        }

        return voteAverage;
    }
}
