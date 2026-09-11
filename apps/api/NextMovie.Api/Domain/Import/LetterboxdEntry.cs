namespace NextMovie.Api.Domain.Import;

/// <summary>
/// One row of a Letterboxd export, in our terms.
/// </summary>
/// <remarks>
/// Already across the boundary: the CSV's column names, its date format and its
/// rating scale are the reader's problem, not this type's. Letterboxd rates on
/// 0.5–5.0 in half-steps, which is identical to ours (ADR-0006), so the rating
/// arrives as a copy rather than a conversion.
/// </remarks>
/// <param name="Name">Film title as Letterboxd holds it.</param>
/// <param name="Year">Release year Letterboxd records, which is often the premiere rather than the release.</param>
/// <param name="FilmUri">
/// Letterboxd's URI for the <b>film</b>. Stable across exports, which is what
/// makes a re-import idempotent — unlike the diary export's URI, which identifies
/// an individual viewing.
/// </param>
/// <param name="Rating">The user's rating, when the export carries one.</param>
/// <param name="WatchedOn">The date on the row, when there is one.</param>
public sealed record LetterboxdEntry(
    string Name,
    int? Year,
    string? FilmUri,
    decimal? Rating,
    DateOnly? WatchedOn);
