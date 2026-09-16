using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Recommendations;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="RecommendationEvent"/> to the <c>recommendation_events</c> table.</summary>
public sealed class RecommendationEventConfiguration : IEntityTypeConfiguration<RecommendationEvent>
{
    public void Configure(EntityTypeBuilder<RecommendationEvent> builder)
    {
        builder.HasKey(served => served.Id);

        builder.Property(served => served.Confidence)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // A native text array: the reasons are read as a whole and never queried
        // individually, so a table would add a join for a query nobody writes.
        builder.Property(served => served.Reasons).HasColumnType("text[]");

        // The query any analysis starts with: what was served to this person, most
        // recent first.
        builder.HasIndex(served => new { served.UserId, served.ServedAt });

        // And the one a model trains on: every time this film was recommended.
        builder.HasIndex(served => served.MovieId);

        builder
            .HasOne(served => served.User)
            .WithMany()
            .HasForeignKey(served => served.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(served => served.Movie)
            .WithMany()
            .HasForeignKey(served => served.MovieId)
            // A film is shared catalogue data; deleting one must not erase the
            // record of what was recommended.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="RecommendationResponse"/> to the <c>recommendation_responses</c> table.</summary>
public sealed class RecommendationResponseConfiguration : IEntityTypeConfiguration<RecommendationResponse>
{
    public void Configure(EntityTypeBuilder<RecommendationResponse> builder)
    {
        builder.HasKey(response => response.Id);

        builder.Property(response => response.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // One current answer per person per film. Without this the exclusion rule
        // and the watchlist could both be right and disagree.
        builder.HasIndex(response => new { response.UserId, response.MovieId }).IsUnique();

        // The watchlist reads exactly this: one person's saved films, newest
        // first.
        builder.HasIndex(response => new { response.UserId, response.Kind, response.RespondedAt });

        builder
            .HasOne(response => response.User)
            .WithMany()
            .HasForeignKey(response => response.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(response => response.Movie)
            .WithMany()
            .HasForeignKey(response => response.MovieId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(response => response.RecommendationEvent)
            .WithMany()
            .HasForeignKey(response => response.RecommendationEventId)

            // The response outlives the impression it came from. Losing the
            // attribution is a loss of training detail; losing the response would
            // be a loss of what the person actually told us.
            .OnDelete(DeleteBehavior.SetNull);
    }
}
