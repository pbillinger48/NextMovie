using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="WatchHistoryEntry"/> to the <c>watch_history</c> table.</summary>
public sealed class WatchHistoryEntryConfiguration : IEntityTypeConfiguration<WatchHistoryEntry>
{
    public void Configure(EntityTypeBuilder<WatchHistoryEntry> builder)
    {
        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Source)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Deliberately NOT unique on (UserId, MovieId): rewatches are the point
        // of keeping a log rather than a flag.
        builder.HasIndex(entry => new { entry.UserId, entry.MovieId });

        builder
            .HasOne(entry => entry.User)
            .WithMany()
            .HasForeignKey(entry => entry.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(entry => entry.Movie)
            .WithMany()
            .HasForeignKey(entry => entry.MovieId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
