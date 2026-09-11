namespace NextMovie.Api.Domain.Import;

/// <summary>
/// A film the catalogue could match a Letterboxd row to.
/// </summary>
/// <remarks>
/// Deliberately not a TMDb DTO. The matching rules are business policy and must
/// be testable without a TMDb response in the way — and without the ability to
/// drift when TMDb changes its wire format.
/// </remarks>
/// <param name="TmdbId">TMDb identifier.</param>
/// <param name="Title">Title as TMDb holds it.</param>
/// <param name="ReleaseYear">Release year, when TMDb knows one.</param>
/// <param name="VoteCount">
/// How many people have rated it on TMDb. The spike found this separates a real
/// film from the obscure short sharing its name and year, decisively and by
/// margins better than 10:1.
/// </param>
public sealed record MatchCandidate(int TmdbId, string Title, int? ReleaseYear, int VoteCount);
