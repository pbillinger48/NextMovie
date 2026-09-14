using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain.Import;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ImportItem"/> to the <c>import_items</c> table.</summary>
public sealed class ImportItemConfiguration : IEntityTypeConfiguration<ImportItem>
{
    public void Configure(EntityTypeBuilder<ImportItem> builder)
    {
        builder.HasKey(item => item.Id);

        // Letterboxd titles are short, but this is user-supplied data from a file
        // and the column should not be the thing that decides how long a title
        // may be.
        builder.Property(item => item.Name).HasMaxLength(500).IsRequired();

        builder.Property(item => item.FilmUri).HasMaxLength(500);

        // Same numeric(2,1) as ratings: this value is copied straight onto one.
        builder.Property(item => item.Rating).HasColumnType("numeric(2,1)");

        builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(item => item.MatchMethod)
            .HasConversion<string>()
            .HasMaxLength(20);

        // A native PostgreSQL integer array rather than a join table: these ids
        // are an opaque list read only as a whole, by one screen, and a table
        // would add a join for no query anyone will write.
        builder.Property(item => item.CandidateTmdbIds).HasColumnType("integer[]");

        // What the worker asks for: the next unresolved item of this job. Also
        // what makes a resumed job skip the work it already did.
        builder.HasIndex(item => new { item.ImportJobId, item.Status });

        builder
            .HasOne(item => item.ImportJob)
            .WithMany(job => job.Items)
            .HasForeignKey(item => item.ImportJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(item => item.MatchedMovie)
            .WithMany()
            .HasForeignKey(item => item.MatchedMovieId)
            // A film is shared catalogue data; deleting one must not erase the
            // audit trail of everybody's imports.
            .OnDelete(DeleteBehavior.Restrict);
    }
}
