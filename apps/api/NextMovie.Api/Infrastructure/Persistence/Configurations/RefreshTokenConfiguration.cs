using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="RefreshToken"/> to the <c>refresh_tokens</c> table.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    // SHA-256 rendered as hex is always exactly 64 characters.
    private const int Sha256HexLength = 64;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash)
            .HasMaxLength(Sha256HexLength)
            .IsFixedLength()
            .IsRequired();

        // Every refresh is a lookup by hash, so this index is on the hot path.
        // Unique because a collision would mean two users could present the same
        // token — the database should refuse that rather than pick a winner.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Revoking a family is a single ranged delete/update; without this index
        // it would scan the whole table at exactly the moment we have detected a
        // possible token theft and want to respond fast.
        builder.HasIndex(t => t.FamilyId);

        // Supports "sign out everywhere" and expired-token cleanup.
        builder.HasIndex(t => t.UserId);

        builder
            .HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            // Deleting a user must not leave usable tokens behind.
            .OnDelete(DeleteBehavior.Cascade);
    }
}
