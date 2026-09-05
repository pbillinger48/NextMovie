using NextMovie.Api.Infrastructure.Tmdb;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Tests.Infrastructure.Tmdb;

/// <summary>
/// Tests for the TMDb anti-corruption boundary.
/// </summary>
/// <remarks>
/// These are the tests worth having at this stage. The mapper is a pure function
/// over a third party's wire format, and every case below is a real behaviour of
/// the TMDb API rather than a hypothetical — empty-string dates, zero ratings
/// meaning "unrated", and fields that only exist on the details endpoint.
/// </remarks>
public sealed class TmdbMovieMapperTests
{
    private static TmdbMovieDto Valid(Action<TmdbMovieDtoBuilder>? customise = null)
    {
        var builder = new TmdbMovieDtoBuilder();
        customise?.Invoke(builder);
        return builder.Build();
    }

    [Fact]
    public void Maps_a_complete_result()
    {
        var mapped = TmdbMovieMapper.ToDomain(Valid());

        Assert.NotNull(mapped);
        Assert.Equal(329865, mapped.Movie.TmdbId);
        Assert.Equal("Arrival", mapped.Movie.Title);
        Assert.Equal(new DateOnly(2016, 11, 10), mapped.Movie.ReleaseDate);
        Assert.Equal(7.6, mapped.Movie.AverageRating);
        Assert.Equal("en", mapped.Movie.Language);
        Assert.Equal([878, 18], mapped.GenreIds);
    }

    [Fact]
    public void Generates_a_version_7_uuid()
    {
        var mapped = TmdbMovieMapper.ToDomain(Valid());

        Assert.NotNull(mapped);
        Assert.NotEqual(Guid.Empty, mapped.Movie.Id);

        // Version nibble lives in the high 4 bits of byte 7 of the RFC 9562
        // layout. Guarding it because silently reverting to v4 would cost the
        // index locality the key was chosen for, with no other visible symptom.
        var version = (mapped.Movie.Id.ToByteArray(bigEndian: true)[6] & 0xF0) >> 4;
        Assert.Equal(7, version);
    }

    // TMDb sends "" rather than null for films with no announced release date.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not-a-date")]
    [InlineData("2016-13-45")]
    public void Treats_unusable_release_dates_as_absent(string? releaseDate)
    {
        var mapped = TmdbMovieMapper.ToDomain(Valid(b => b.ReleaseDate = releaseDate));

        Assert.NotNull(mapped);
        Assert.Null(mapped.Movie.ReleaseDate);
    }

    [Fact]
    public void Treats_an_unvoted_film_as_unrated_rather_than_zero()
    {
        // TMDb reports vote_average 0 when nobody has voted. Persisting a literal
        // 0.0 would tell the recommendation engine this is the worst film ever
        // made, when we actually know nothing about it.
        var mapped = TmdbMovieMapper.ToDomain(Valid(b =>
        {
            b.VoteAverage = 0;
            b.VoteCount = 0;
        }));

        Assert.NotNull(mapped);
        Assert.Null(mapped.Movie.AverageRating);
    }

    [Fact]
    public void Keeps_a_genuine_rating()
    {
        var mapped = TmdbMovieMapper.ToDomain(Valid(b =>
        {
            b.VoteAverage = 8.2;
            b.VoteCount = 1_500;
        }));

        Assert.NotNull(mapped);
        Assert.Equal(8.2, mapped.Movie.AverageRating);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalises_empty_strings_to_null(string? value)
    {
        var mapped = TmdbMovieMapper.ToDomain(Valid(b =>
        {
            b.Overview = value;
            b.PosterPath = value;
            b.BackdropPath = value;
            b.OriginalTitle = value;
        }));

        Assert.NotNull(mapped);
        Assert.Null(mapped.Movie.Overview);
        Assert.Null(mapped.Movie.PosterPath);
        Assert.Null(mapped.Movie.BackdropPath);
        Assert.Null(mapped.Movie.OriginalTitle);
    }

    [Fact]
    public void Leaves_details_only_fields_absent()
    {
        // Runtime and status do not appear in search results. Defaulting them to
        // 0 or "Released" would be inventing data.
        var mapped = TmdbMovieMapper.ToDomain(Valid());

        Assert.NotNull(mapped);
        Assert.Null(mapped.Movie.Runtime);
        Assert.Null(mapped.Movie.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_a_result_without_a_usable_id(int id)
    {
        Assert.Null(TmdbMovieMapper.ToDomain(Valid(b => b.Id = id)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rejects_a_result_without_a_title(string? title)
    {
        Assert.Null(TmdbMovieMapper.ToDomain(Valid(b => b.Title = title)));
    }

    [Fact]
    public void Trims_surrounding_whitespace_from_the_title()
    {
        var mapped = TmdbMovieMapper.ToDomain(Valid(b => b.Title = "  Arrival  "));

        Assert.NotNull(mapped);
        Assert.Equal("Arrival", mapped.Movie.Title);
    }

    [Fact]
    public void Tolerates_a_result_with_no_genres()
    {
        var mapped = TmdbMovieMapper.ToDomain(Valid(b => b.GenreIds = null));

        Assert.NotNull(mapped);
        Assert.Empty(mapped.GenreIds);
    }

    /// <summary>Mutable builder so each test states only the field it cares about.</summary>
    internal sealed class TmdbMovieDtoBuilder
    {
        public int Id { get; set; } = 329865;
        public string? Title { get; set; } = "Arrival";
        public string? OriginalTitle { get; set; } = "Arrival";
        public string? Overview { get; set; } = "A linguist is recruited to communicate with alien visitors.";
        public string? PosterPath { get; set; } = "/poster.jpg";
        public string? BackdropPath { get; set; } = "/backdrop.jpg";
        public string? ReleaseDate { get; set; } = "2016-11-10";
        public double? VoteAverage { get; set; } = 7.6;
        public int VoteCount { get; set; } = 18_000;
        public double? Popularity { get; set; } = 45.2;
        public string? OriginalLanguage { get; set; } = "en";
        public IReadOnlyList<int>? GenreIds { get; set; } = [878, 18];

        public TmdbMovieDto Build() => new()
        {
            Id = Id,
            Title = Title,
            OriginalTitle = OriginalTitle,
            Overview = Overview,
            PosterPath = PosterPath,
            BackdropPath = BackdropPath,
            ReleaseDate = ReleaseDate,
            VoteAverage = VoteAverage,
            VoteCount = VoteCount,
            Popularity = Popularity,
            OriginalLanguage = OriginalLanguage,
            GenreIds = GenreIds,
        };
    }
}
