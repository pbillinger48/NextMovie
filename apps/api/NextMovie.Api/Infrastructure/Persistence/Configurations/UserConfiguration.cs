using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="User"/> to the <c>users</c> table.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    // RFC 5321 caps a full address at 254 characters; 320 (64 local + @ + 255
    // domain) is the theoretical maximum and the safer bound to store.
    private const int MaxEmailLength = 320;

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).HasMaxLength(MaxEmailLength).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(MaxEmailLength).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();

        // Sized generously rather than to today's output: PasswordHasher's
        // format is versioned, and a future algorithm with a longer hash should
        // not require a migration on a live users table.
        builder.Property(u => u.PasswordHash).HasMaxLength(512);

        builder.Property(u => u.ProfileImageUrl).HasMaxLength(2048);

        // Uniqueness lives on the normalised column, so Parker@x.com cannot
        // register alongside parker@x.com.
        //
        // NOTE: this is a plain unique index, correct because Milestone 2 has no
        // account deletion. If soft delete is ever added, a deleted user's
        // address would be permanently unusable — that change must also convert
        // this to a partial index (WHERE deleted_at IS NULL), which is a
        // migration against live data.
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}
