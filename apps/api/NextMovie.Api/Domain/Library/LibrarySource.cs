namespace NextMovie.Api.Domain.Library;

/// <summary>Where a rating or a viewing came from.</summary>
/// <remarks>
/// The reason a re-import can leave hand-entered data alone (ADR-0006). Persisted
/// by name rather than by number, so the column is readable in psql and so
/// reordering this enum cannot silently relabel every existing row.
/// </remarks>
public enum LibrarySource
{
    /// <summary>Entered in NextMovie by the user.</summary>
    Native = 1,

    /// <summary>Created by a Letterboxd CSV import.</summary>
    LetterboxdImport = 2,
}
