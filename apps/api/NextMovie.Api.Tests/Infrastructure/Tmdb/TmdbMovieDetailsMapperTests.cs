using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Tests.Infrastructure.Tmdb;

/// <summary>
/// Tests the anti-corruption boundary for TMDb's details endpoint.
/// </summary>
/// <remarks>
/// The details response carries three things search cannot — runtime, status and
/// full genre objects — and each has a TMDb convention worth correcting once,
/// here, rather than everywhere downstream.
/// </remarks>
public sealed class TmdbMovieDetailsMapperTests
{
    private static readonly DateTimeOffset RefreshedAt = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static TmdbMovieDetailsResponse Details(
        int id = 27205,
        string? title = "Inception",
        int? runtime = 148,
        string? status = "Released") => new()
    {
        Id = id,
        Title = title,
        OriginalTitle = "Inception",
        Overview = "A thief who steals corporate secrets.",
        PosterPath = "/poster.jpg",
        ReleaseDate = "2010-07-15",
        Runtime = runtime,
        VoteAverage = 8.4,
        VoteCount = 34000,
        Popularity = 82.3,
        OriginalLanguage = "en",
        Status = status,
        Genres = [new TmdbGenreDto { Id = 28, Name = "Action" }, new TmdbGenreDto { Id = 878, Name = "Science Fiction" }],
    };

    [Fact]
    public void Maps_the_fields_search_cannot_supply()
    {
        var mapped = TmdbMovieMapper.ToDomain(Details(), RefreshedAt);

        Assert.NotNull(mapped);
        Assert.Equal(148, mapped.Movie.Runtime);
        Assert.Equal("Released", mapped.Movie.Status);

        // The stamp is what marks the film as enriched. Without it every request
        // would re-fetch, putting a TMDb round trip on every read.
        Assert.Equal(RefreshedAt, mapped.Movie.DetailsRefreshedAt);
    }

    [Fact]
    public void Treats_a_zero_runtime_as_unknown()
    {
        // TMDb reports 0 for films whose runtime it does not know. Storing that
        // claims the film is zero minutes long, which is a different statement.
        var mapped = TmdbMovieMapper.ToDomain(Details(runtime: 0), RefreshedAt);

        Assert.NotNull(mapped);
        Assert.Null(mapped.Movie.Runtime);
    }

    [Fact]
    public void Treats_a_missing_runtime_as_unknown()
    {
        var mapped = TmdbMovieMapper.ToDomain(Details(runtime: null), RefreshedAt);

        Assert.NotNull(mapped);
        Assert.Null(mapped.Movie.Runtime);
    }

    [Fact]
    public void Takes_genre_ids_from_the_full_objects()
    {
        var mapped = TmdbMovieMapper.ToDomain(Details(), RefreshedAt);

        Assert.NotNull(mapped);

        // Only the ids: genre names are reference data we seed ourselves, so
        // TMDb's spelling cannot drift into our catalogue.
        Assert.Equal([28, 878], mapped.GenreIds);
    }

    [Fact]
    public void Tolerates_a_film_with_no_genres()
    {
        var mapped = TmdbMovieMapper.ToDomain(Details() with { Genres = null }, RefreshedAt);

        Assert.NotNull(mapped);
        Assert.Empty(mapped.GenreIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_record_with_no_usable_id(int id)
    {
        // Without a TMDb id the row cannot be deduplicated or refreshed later.
        Assert.Null(TmdbMovieMapper.ToDomain(Details(id: id), RefreshedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_record_with_no_title(string? title)
    {
        Assert.Null(TmdbMovieMapper.ToDomain(Details(title: title), RefreshedAt));
    }

    [Fact]
    public void Applies_the_same_conventions_as_the_search_mapper()
    {
        var mapped = TmdbMovieMapper.ToDomain(
            Details() with { ReleaseDate = "", VoteAverage = 0, VoteCount = 0, Status = "  " },
            RefreshedAt);

        Assert.NotNull(mapped);

        // Empty-string dates, unvoted ratings and whitespace strings are TMDb
        // conventions that the details path must correct exactly as search does —
        // two mappers disagreeing would be worse than one being wrong.
        Assert.Null(mapped.Movie.ReleaseDate);
        Assert.Null(mapped.Movie.AverageRating);
        Assert.Null(mapped.Movie.Status);
    }
}
