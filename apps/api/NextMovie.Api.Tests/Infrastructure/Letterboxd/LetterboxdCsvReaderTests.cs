using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NextMovie.Api.Infrastructure.Letterboxd;

namespace NextMovie.Api.Tests.Infrastructure.Letterboxd;

/// <summary>
/// Tests the Letterboxd export boundary.
/// </summary>
/// <remarks>
/// The failure mode worth defending against is not a crash but a mis-split row —
/// a title containing a comma shifting every later column, so a rating lands on
/// the wrong film. That is invisible once imported, which is why a real CSV
/// parser is a dependency worth having.
/// </remarks>
public sealed class LetterboxdCsvReaderTests
{
    private static LetterboxdCsvReadResult Read(string csv)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        return new LetterboxdCsvReader(NullLogger<LetterboxdCsvReader>.Instance).Read(stream);
    }

    [Fact]
    public void Reads_a_watched_export()
    {
        // The exact shape quoted in the spike.
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI
            2022-01-02,Don't Look Up,2021,https://boxd.it/o0Hc
            """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Don't Look Up", entry.Name);
        Assert.Equal(2021, entry.Year);
        Assert.Equal("https://boxd.it/o0Hc", entry.FilmUri);
        Assert.Equal(new DateOnly(2022, 1, 2), entry.WatchedOn);
        Assert.Null(entry.Rating);
    }

    [Fact]
    public void Reads_a_ratings_export()
    {
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI,Rating
            2022-01-02,Don't Look Up,2021,https://boxd.it/o0Hc,4
            """);

        // Letterboxd rates 0.5-5.0 in half-steps, identical to ours, so this is
        // a copy rather than a conversion.
        Assert.Equal(4m, Assert.Single(result.Entries).Rating);
    }

    [Fact]
    public void Reads_a_half_star_rating()
    {
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI,Rating
            2022-01-02,Inception,2010,https://boxd.it/abc,4.5
            """);

        Assert.Equal(4.5m, Assert.Single(result.Entries).Rating);
    }

    [Fact]
    public void Keeps_a_title_containing_a_comma_intact()
    {
        // The whole reason a real parser earns its dependency. Split naively,
        // this row shifts every later column and the rating lands on the year.
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI,Rating
            2022-01-02,"Crouching Tiger, Hidden Dragon",2000,https://boxd.it/abc,5
            """);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("Crouching Tiger, Hidden Dragon", entry.Name);
        Assert.Equal(2000, entry.Year);
        Assert.Equal(5m, entry.Rating);
    }

    [Fact]
    public void Keeps_a_title_containing_quotes_intact()
    {
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI
            2022-01-02,"The ""Human"" Centipede",2009,https://boxd.it/abc
            """);

        Assert.Equal("The \"Human\" Centipede", Assert.Single(result.Entries).Name);
    }

    [Fact]
    public void Keeps_non_ascii_titles_intact()
    {
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI
            2022-01-02,Amélie,2001,https://boxd.it/abc
            """);

        Assert.Equal("Amélie", Assert.Single(result.Entries).Name);
    }

    [Fact]
    public void Counts_rows_with_no_title_rather_than_dropping_them_silently()
    {
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI
            2022-01-02,,2021,https://boxd.it/o0Hc
            2022-01-03,Inception,2010,https://boxd.it/abc
            """);

        // A user who exported 800 films and imported 799 deserves to be told
        // which number is which.
        Assert.Single(result.Entries);
        Assert.Equal(1, result.SkippedRows);
    }

    [Fact]
    public void Keeps_a_row_whose_rating_cannot_be_read()
    {
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI,Rating
            2022-01-02,Inception,2010,https://boxd.it/abc,not-a-number
            """);

        // The viewing is still worth importing; losing the film over a malformed
        // cell would cost more than it protects.
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Inception", entry.Name);
        Assert.Null(entry.Rating);
    }

    [Theory]
    [InlineData("not-a-year")]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("3025")]
    public void Treats_an_implausible_year_as_unknown(string year)
    {
        var result = Read(
            $"""
            Date,Name,Year,Letterboxd URI
            2022-01-02,Inception,{year},https://boxd.it/abc
            """);

        // Unknown, not zero: the matcher falls back to title-only matching, which
        // is better than excluding every candidate by comparing against a year
        // that never existed.
        Assert.Null(Assert.Single(result.Entries).Year);
    }

    [Fact]
    public void Prefers_the_diary_watched_date_over_the_logged_date()
    {
        // diary.csv distinguishes when a film was watched from when the entry was
        // created. The viewing date is the one that means something.
        var result = Read(
            """
            Date,Name,Year,Letterboxd URI,Rating,Watched Date
            2024-05-01,Inception,2010,https://boxd.it/abc,4.5,2024-04-28
            """);

        Assert.Equal(new DateOnly(2024, 4, 28), Assert.Single(result.Entries).WatchedOn);
    }

    [Fact]
    public void Tolerates_an_export_with_no_rating_column()
    {
        var result = Read(
            """
            Name,Year
            Inception,2010
            """);

        // watched.csv has no Rating column at all. A missing column is a file of
        // unrated viewings, not an error.
        var entry = Assert.Single(result.Entries);
        Assert.Equal("Inception", entry.Name);
        Assert.Null(entry.Rating);
        Assert.Null(entry.FilmUri);
    }

    [Fact]
    public void Tolerates_an_empty_file()
    {
        var result = Read(string.Empty);

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.SkippedRows);
    }

    [Fact]
    public void Tolerates_a_header_with_no_rows()
    {
        Assert.Empty(Read("Date,Name,Year,Letterboxd URI").Entries);
    }

    [Fact]
    public void Reads_windows_line_endings()
    {
        // A file opened and re-saved in Excel on Windows.
        var result = Read("Date,Name,Year\r\n2022-01-02,Inception,2010\r\n");

        Assert.Equal("Inception", Assert.Single(result.Entries).Name);
    }
}
