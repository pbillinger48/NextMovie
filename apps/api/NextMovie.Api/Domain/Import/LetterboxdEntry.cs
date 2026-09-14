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
/// <param name="IsLoggedViewing">
/// Whether this row records an individual viewing, as <c>diary.csv</c> does,
/// rather than membership of a list.
/// </param>
/// <remarks>
/// <para>
/// The distinction in <paramref name="IsLoggedViewing"/> matters more than it
/// looks. <c>watched.csv</c> and <c>ratings.csv</c> list films — one row per film
/// — and their <c>Date</c> column records when the row was created, not when the
/// film was seen. <c>diary.csv</c> records viewings, one row each, and a rewatch
/// is genuinely two rows.
/// </para>
/// <para>
/// Conflating them imports phantom rewatches: the same film appears in both
/// files with different dates, and a viewing keyed on the date becomes two. That
/// happened to 49 films on a real 796-film library before this existed.
/// </para>
/// </remarks>
public sealed record LetterboxdEntry(
    string Name,
    int? Year,
    string? FilmUri,
    decimal? Rating,
    DateOnly? WatchedOn,
    bool IsLoggedViewing = false);
