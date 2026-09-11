using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using NextMovie.Api.Domain.Import;

namespace NextMovie.Api.Infrastructure.Letterboxd;

/// <summary>
/// Reads a Letterboxd export into domain rows.
/// </summary>
/// <remarks>
/// The anti-corruption boundary for Letterboxd, in the same spirit as
/// <c>TmdbMovieMapper</c>: column names, date formats and absent columns are
/// dealt with once, here, and never leak inward.
/// <para>
/// One reader serves <c>watched.csv</c>, <c>ratings.csv</c> and <c>diary.csv</c>,
/// which share the columns that matter and differ in which extras they carry.
/// Fields are read by name and missing ones are tolerated, so a file without a
/// <c>Rating</c> column is a file of unrated viewings rather than an error.
/// </para>
/// </remarks>
internal sealed class LetterboxdCsvReader(ILogger<LetterboxdCsvReader> logger)
{
    private const string NameColumn = "Name";
    private const string YearColumn = "Year";
    private const string UriColumn = "Letterboxd URI";
    private const string RatingColumn = "Rating";
    private const string DateColumn = "Date";
    private const string WatchedDateColumn = "Watched Date";

    /// <summary>Reads every usable row.</summary>
    /// <remarks>
    /// Materialises the whole file. A Letterboxd export is a few thousand rows of
    /// short strings — well under a megabyte — and streaming would complicate the
    /// caller for no measurable gain. If exports ever grow by an order of
    /// magnitude this is the thing to revisit.
    /// </remarks>
    public LetterboxdCsvReadResult Read(Stream csv)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            // A Letterboxd export is a file a person uploaded. It may have been
            // opened in Excel, re-saved, or trimmed by hand — so unknown and
            // missing columns are tolerated rather than fatal.
            HeaderValidated = null,
            MissingFieldFound = null,
            TrimOptions = TrimOptions.Trim,
            DetectDelimiter = false,
        };

        using var reader = new StreamReader(csv);
        using var parser = new CsvReader(reader, configuration);

        var entries = new List<LetterboxdEntry>();
        var skipped = 0;

        if (!parser.Read() || !parser.ReadHeader())
        {
            logger.LogWarning("A Letterboxd export had no header row");

            return new LetterboxdCsvReadResult([], 0);
        }

        while (parser.Read())
        {
            var name = Field(parser, NameColumn);

            if (string.IsNullOrWhiteSpace(name))
            {
                // Without a title there is nothing to match on. Counted rather
                // than dropped silently, because a user who exported 800 films
                // and imported 790 deserves to be told.
                skipped++;
                continue;
            }

            entries.Add(new LetterboxdEntry(
                Name: name,
                Year: ParseYear(Field(parser, YearColumn)),
                FilmUri: Field(parser, UriColumn),

                // Letterboxd rates 0.5-5.0 in half-steps, which is exactly our
                // scale (ADR-0006), so this is a copy and not a conversion.
                Rating: ParseRating(Field(parser, RatingColumn)),

                // diary.csv distinguishes the day a film was watched from the day
                // the entry was logged; the plain exports only have the latter.
                WatchedOn: ParseDate(Field(parser, WatchedDateColumn) ?? Field(parser, DateColumn))));
        }

        if (skipped > 0)
        {
            logger.LogInformation("Skipped {Skipped} Letterboxd rows with no title", skipped);
        }

        return new LetterboxdCsvReadResult(entries, skipped);
    }

    /// <summary>Reads a field by name, treating an absent column as an absent value.</summary>
    private static string? Field(CsvReader parser, string column) =>
        parser.TryGetField<string>(column, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static int? ParseYear(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            && year is > 1800 and < 2200
            ? year
            : null;

    /// <remarks>
    /// An unparseable rating becomes "no rating" rather than failing the row: the
    /// viewing is still worth importing, and refusing the whole film over a
    /// malformed cell would lose more than it protects.
    /// </remarks>
    private static decimal? ParseRating(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var rating)
            ? rating
            : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}

/// <summary>What a Letterboxd export yielded.</summary>
/// <param name="Entries">Rows carrying at least a title.</param>
/// <param name="SkippedRows">
/// Rows with no title, which cannot be matched. Surfaced so an import can tell
/// the user what it did not read, rather than quietly returning a smaller number
/// than they exported.
/// </param>
internal sealed record LetterboxdCsvReadResult(IReadOnlyList<LetterboxdEntry> Entries, int SkippedRows);
