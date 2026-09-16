using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NextMovie.Api.Domain.Streaming;

namespace NextMovie.Api.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="StreamingProvider"/> to the <c>streaming_providers</c> table.</summary>
public sealed class StreamingProviderConfiguration : IEntityTypeConfiguration<StreamingProvider>
{
    public void Configure(EntityTypeBuilder<StreamingProvider> builder)
    {
        // TMDb's identifier is the key, as with genres: stable, shared, and
        // already the number every response speaks in.
        builder.HasKey(provider => provider.Id);
        builder.Property(provider => provider.Id).ValueGeneratedNever();

        builder.Property(provider => provider.Name).HasMaxLength(200).IsRequired();
        builder.Property(provider => provider.LogoPath).HasMaxLength(500);
    }
}

/// <summary>Maps <see cref="MovieAvailability"/> to the <c>movie_availability</c> table.</summary>
public sealed class MovieAvailabilityConfiguration : IEntityTypeConfiguration<MovieAvailability>
{
    public void Configure(EntityTypeBuilder<MovieAvailability> builder)
    {
        builder.HasKey(availability => availability.Id);

        // Two characters, fixed: ISO 3166-1 alpha-2 and nothing else.
        builder.Property(availability => availability.Region)
            .HasMaxLength(2)
            .IsFixedLength()
            .IsRequired();

        builder.Property(availability => availability.Link).HasMaxLength(1000);

        // One answer per film per region. Without this the cache would
        // accumulate duplicate answers and "which is current" becomes a guess.
        builder.HasIndex(availability => new { availability.MovieId, availability.Region }).IsUnique();

        builder
            .HasOne(availability => availability.Movie)
            .WithMany()
            .HasForeignKey(availability => availability.MovieId)
            // Availability is about a film; without the film it means nothing.
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="AvailabilityOffer"/> to the <c>availability_offers</c> table.</summary>
public sealed class AvailabilityOfferConfiguration : IEntityTypeConfiguration<AvailabilityOffer>
{
    public void Configure(EntityTypeBuilder<AvailabilityOffer> builder)
    {
        // Named explicitly. Offers are reached through their availability rather
        // than a DbSet of their own, and without this EF names the table from the
        // entity — leaving one singular table among a schema of plural ones.
        builder.ToTable("availability_offers");

        builder.HasKey(offer => offer.Id);

        builder.Property(offer => offer.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // A service can offer the same film two ways — rent and buy from one
        // shop — and each is a distinct way to watch, so the pair is what must be
        // unique rather than the provider alone.
        builder
            .HasIndex(offer => new { offer.MovieAvailabilityId, offer.StreamingProviderId, offer.Type })
            .IsUnique();

        builder
            .HasOne(offer => offer.MovieAvailability)
            .WithMany(availability => availability.Offers)
            .HasForeignKey(offer => offer.MovieAvailabilityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(offer => offer.StreamingProvider)
            .WithMany()
            .HasForeignKey(offer => offer.StreamingProviderId)
            // Providers are reference data shared by every film's availability.
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="UserStreamingProvider"/> to the <c>user_streaming_providers</c> table.</summary>
public sealed class UserStreamingProviderConfiguration : IEntityTypeConfiguration<UserStreamingProvider>
{
    public void Configure(EntityTypeBuilder<UserStreamingProvider> builder)
    {
        // The pair is the identity: somebody either subscribes to a service or
        // does not, and a surrogate key would allow saying so twice.
        builder.HasKey(subscription => new
        {
            subscription.UserId,
            subscription.StreamingProviderId,
        });

        builder
            .HasOne(subscription => subscription.User)
            .WithMany(user => user.StreamingProviders)
            .HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(subscription => subscription.StreamingProvider)
            .WithMany()
            .HasForeignKey(subscription => subscription.StreamingProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
