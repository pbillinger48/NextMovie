using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain.Import;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="ImportJob"/> to the <c>import_jobs</c> table.</summary>
public sealed class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.HasKey(job => job.Id);

        builder.Property(job => job.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(job => job.FailureReason).HasMaxLength(500);

        // The queue's own index: the worker asks for the oldest pending job on
        // every pass, so this is the hottest query in the import.
        builder.HasIndex(job => new { job.Status, job.CreatedAt });

        // Supports "show me my imports" without scanning everyone's.
        builder.HasIndex(job => job.UserId);

        builder
            .HasOne(job => job.User)
            .WithMany()
            .HasForeignKey(job => job.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
