using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="UserExternalLogin"/> to the <c>user_external_logins</c> table.</summary>
public sealed class UserExternalLoginConfiguration : IEntityTypeConfiguration<UserExternalLogin>
{
    // Google documents `sub` as at most 255 characters, and other providers are
    // unlikely to exceed it.
    private const int MaxProviderSubjectLength = 255;

    private const int MaxProviderNameLength = 50;

    public void Configure(EntityTypeBuilder<UserExternalLogin> builder)
    {
        builder.HasKey(login => login.Id);

        builder.Property(login => login.Provider)
            .HasConversion<string>()
            .HasMaxLength(MaxProviderNameLength)
            .IsRequired();

        builder.Property(login => login.ProviderSubject)
            .HasMaxLength(MaxProviderSubjectLength)
            .IsRequired();

        // One Google identity belongs to exactly one account. Without this the
        // database would happily let the same person be linked to two accounts,
        // and which one they signed into would come down to query ordering.
        builder
            .HasIndex(login => new { login.Provider, login.ProviderSubject })
            .IsUnique();

        builder
            .HasOne(login => login.User)
            .WithMany(user => user.ExternalLogins)
            .HasForeignKey(login => login.UserId)
            // Deleting a user must not leave an identity pointing at nothing.
            .OnDelete(DeleteBehavior.Cascade);
    }
}
