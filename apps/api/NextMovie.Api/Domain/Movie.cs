namespace NextMovie.Api.Domain;

/// <summary>
/// A single film. Metadata originates from TMDb but is copied into our own
/// schema rather than referenced live, so the catalogue stays queryable and
/// recommendations do not depend on a third party being reachable.
/// </summary>
public class Movie
{
    /// <summary>
    /// Surrogate key, deliberately not TMDb's identifier: the primary key must
    /// stay ours so a film TMDb does not carry (or a change of metadata source)
    /// does not become a schema migration.
    /// </summary>
    /// <remarks>
    /// UUIDv7 rather than v4. The leading timestamp makes inserts land near the
    /// right edge of the index instead of scattering across it, which matters on
    /// the bulk-insert path a Letterboxd import takes.
    /// </remarks>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>TMDb's identifier for this film. Unique; the guard against duplicate rows on import.</summary>
    public required int TmdbId { get; init; }

    /// <summary>Display title, in the catalogue's language.</summary>
    public required string Title { get; set; }

    /// <summary>Title in the film's original language, when it differs.</summary>
    public string? OriginalTitle { get; set; }

    /// <summary>Synopsis. Absent for many obscure or unreleased films.</summary>
    public string? Overview { get; set; }

    /// <summary>Relative TMDb poster path, e.g. <c>/abc123.jpg</c>. Combined with a base URL at render time.</summary>
    public string? PosterPath { get; set; }

    /// <summary>Relative TMDb backdrop path.</summary>
    public string? BackdropPath { get; set; }

    /// <summary>
    /// Release date. A date with no time component, so <see cref="DateOnly"/>
    /// rather than a timestamp: this is a calendar fact, not an instant, and
    /// modelling it as a timestamp invites time-zone bugs that shift a film's
    /// release by a day.
    /// </summary>
    public DateOnly? ReleaseDate { get; set; }

    /// <summary>Runtime in minutes. Frequently missing for unreleased films.</summary>
    public int? Runtime { get; set; }

    /// <summary>TMDb community rating, 0–10. Not a NextMovie user rating.</summary>
    public double? AverageRating { get; set; }

    /// <summary>TMDb popularity score. Relative and unbounded; only meaningful compared against other films.</summary>
    public double? Popularity { get; set; }

    /// <summary>ISO 639-1 code of the original language, e.g. <c>en</c>.</summary>
    public string? Language { get; set; }

    /// <summary>TMDb release status, e.g. <c>Released</c>, <c>Post Production</c>.</summary>
    public string? Status { get; set; }

    /// <summary>Genres this film belongs to.</summary>
    public ICollection<Genre> Genres { get; init; } = [];

    /// <summary>When this row was first written.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When this row was last refreshed from TMDb.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
