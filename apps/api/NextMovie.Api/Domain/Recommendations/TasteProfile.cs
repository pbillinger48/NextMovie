namespace NextMovie.Api.Domain.Recommendations;

/// <summary>
/// What someone's viewing and rating say about what they want to watch.
/// </summary>
/// <remarks>
/// Computed per request rather than stored (ADR-0008).
/// <para>
/// Built from two kinds of evidence, because they answer different questions.
/// <b>What you watch</b> reveals what you are interested in; <b>how you rate it</b>
/// reveals how well that interest is repaid. A model built on ratings alone
/// concludes that somebody who has watched 97 animated films and 11 westerns
/// dislikes animation — which is exactly what an earlier version of this said
/// about a real library, and why it now reads both.
/// </para>
/// </remarks>
/// <param name="GenreAffinity">How much this person wants films of each genre, 0–1. Half is no opinion.</param>
/// <param name="GenreAppetite">How much of their viewing each genre accounts for, 0–1.</param>
/// <param name="GenreUpside">How reliably each genre produces a film they love, 0–1.</param>
/// <param name="AverageRating">Their overall average rating.</param>
/// <param name="RatedFilms">How many ratings the profile was built from. Drives confidence.</param>
/// <param name="PreferredRuntimes">The middle of the runtime range of films they rate highly.</param>
/// <param name="PreferredEra">The middle of the release-year range of films they rate highly.</param>
public sealed record TasteProfile(
    IReadOnlyDictionary<int, double> GenreAffinity,
    IReadOnlyDictionary<int, double> GenreAppetite,
    IReadOnlyDictionary<int, double> GenreUpside,
    double AverageRating,
    int RatedFilms,
    Range<int>? PreferredRuntimes,
    Range<int>? PreferredEra)
{
    /// <summary>What no evidence looks like: no opinion either way.</summary>
    public const double NoOpinion = 0.5;

    /// <summary>A profile for someone with no history at all.</summary>
    public static readonly TasteProfile Empty = new(
        new Dictionary<int, double>(),
        new Dictionary<int, double>(),
        new Dictionary<int, double>(),
        AverageRating: 0,
        RatedFilms: 0,
        PreferredRuntimes: null,
        PreferredEra: null);

    /// <summary>How much this person wants a film of this genre. Half when unknown.</summary>
    public double AffinityFor(int genreId) => GenreAffinity.GetValueOrDefault(genreId, NoOpinion);
}

/// <summary>An inclusive range.</summary>
public sealed record Range<T>(T From, T To)
    where T : IComparable<T>
{
    public bool Contains(T value) => value.CompareTo(From) >= 0 && value.CompareTo(To) <= 0;
}

/// <summary>One film the user has rated.</summary>
/// <param name="Rating">0.5–5.0.</param>
/// <param name="GenreIds">TMDb genre identifiers.</param>
/// <param name="Runtime">Minutes, when known.</param>
/// <param name="ReleaseYear">Year, when known.</param>
public sealed record RatedFilm(
    decimal Rating,
    IReadOnlyList<int> GenreIds,
    int? Runtime,
    int? ReleaseYear);

/// <summary>
/// One film the user has watched, rated or not.
/// </summary>
/// <remarks>
/// Watching is a choice, and the choice is evidence even when no rating follows.
/// </remarks>
/// <param name="GenreIds">TMDb genre identifiers.</param>
public sealed record WatchedFilm(IReadOnlyList<int> GenreIds);
