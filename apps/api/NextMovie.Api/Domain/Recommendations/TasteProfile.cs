namespace NextMovie.Api.Domain.Recommendations;

/// <summary>
/// What someone's ratings say about what they like.
/// </summary>
/// <remarks>
/// Computed per request rather than stored (ADR-0008): it is one pass over a few
/// hundred ratings, and persisting it would buy invalidation logic on every
/// rating, import and viewing in exchange for microseconds.
/// </remarks>
/// <param name="GenreAffinity">
/// Per genre, how much better or worse than usual this person rates it, in rating
/// points. Positive means they like the genre more than they like films
/// generally.
/// </param>
/// <param name="AverageRating">Their overall average, the baseline affinity is measured against.</param>
/// <param name="RatedFilms">How many ratings the profile was built from. Drives confidence.</param>
/// <param name="PreferredRuntimes">The middle of the runtime range of films they rate highly.</param>
/// <param name="PreferredEra">The middle of the release-year range of films they rate highly.</param>
public sealed record TasteProfile(
    IReadOnlyDictionary<int, double> GenreAffinity,
    double AverageRating,
    int RatedFilms,
    Range<int>? PreferredRuntimes,
    Range<int>? PreferredEra)
{
    /// <summary>A profile with nothing in it, for someone who has rated nothing.</summary>
    public static readonly TasteProfile Empty = new(
        new Dictionary<int, double>(),
        AverageRating: 0,
        RatedFilms: 0,
        PreferredRuntimes: null,
        PreferredEra: null);
}

/// <summary>An inclusive range.</summary>
public sealed record Range<T>(T From, T To)
    where T : IComparable<T>
{
    public bool Contains(T value) => value.CompareTo(From) >= 0 && value.CompareTo(To) <= 0;
}

/// <summary>One film the user has rated, with the attributes a profile is built from.</summary>
/// <param name="Rating">0.5–5.0.</param>
/// <param name="GenreIds">TMDb genre identifiers.</param>
/// <param name="Runtime">Minutes, when known.</param>
/// <param name="ReleaseYear">Year, when known.</param>
public sealed record RatedFilm(
    decimal Rating,
    IReadOnlyList<int> GenreIds,
    int? Runtime,
    int? ReleaseYear);
