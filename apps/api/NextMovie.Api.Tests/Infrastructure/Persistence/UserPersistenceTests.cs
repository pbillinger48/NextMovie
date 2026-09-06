using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain;

namespace NextMovie.Api.Tests.Infrastructure.Persistence;

/// <summary>
/// Verifies the constraints protecting accounts, against real PostgreSQL.
/// </summary>
/// <remarks>
/// These are database guarantees, not application logic, so they can only be
/// tested against the real engine — EF's InMemory provider enforces neither
/// unique indexes nor cascades and would pass while production broke.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class UserPersistenceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly CancellationToken Ct =
        new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    public async Task InitializeAsync()
    {
        await using var db = postgres.CreateContext();
        await db.Database.ExecuteSqlRawAsync("delete from refresh_tokens; delete from users;", Ct);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(string email, string name = "Test") =>
        new() { Email = email, DisplayName = name };

    [Fact]
    public async Task Stores_and_reads_back_a_user()
    {
        await using var db = postgres.CreateContext();
        db.Users.Add(NewUser("parker@example.com", "Parker"));
        await db.SaveChangesAsync(Ct);

        var stored = await db.Users.SingleAsync(Ct);
        Assert.Equal("parker@example.com", stored.Email);
        Assert.Equal("PARKER@EXAMPLE.COM", stored.NormalizedEmail);
        Assert.Null(stored.PasswordHash);
    }

    [Fact]
    public async Task Rejects_a_second_account_with_the_same_email_in_a_different_case()
    {
        await using (var first = postgres.CreateContext())
        {
            first.Users.Add(NewUser("Parker@example.com"));
            await first.SaveChangesAsync(Ct);
        }

        await using var second = postgres.CreateContext();
        second.Users.Add(NewUser("parker@EXAMPLE.com"));

        // The database must refuse this, not the application — an application
        // check alone loses to a concurrent registration.
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_refresh_tokens()
    {
        Guid userId;

        await using (var seed = postgres.CreateContext())
        {
            var user = NewUser("parker@example.com");
            user.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = new string('a', 64),
                FamilyId = Guid.CreateVersion7(),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
            });
            seed.Users.Add(user);
            await seed.SaveChangesAsync(Ct);
            userId = user.Id;
        }

        await using var db = postgres.CreateContext();
        db.Users.Remove(await db.Users.SingleAsync(u => u.Id == userId, Ct));
        await db.SaveChangesAsync(Ct);

        // A token outliving its user would still authenticate a deleted account.
        Assert.Equal(0, await db.RefreshTokens.CountAsync(Ct));
    }

    [Fact]
    public async Task Rejects_two_tokens_sharing_a_hash()
    {
        await using var db = postgres.CreateContext();
        var user = NewUser("parker@example.com");
        var family = Guid.CreateVersion7();
        var hash = new string('b', 64);

        user.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, TokenHash = hash, FamilyId = family,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        });
        user.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, TokenHash = hash, FamilyId = family,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        });
        db.Users.Add(user);

        // Two rows with the same hash would make a presented token ambiguous.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task A_revoked_token_is_not_active()
    {
        var now = DateTimeOffset.UtcNow;
        var token = new RefreshToken
        {
            UserId = Guid.CreateVersion7(),
            TokenHash = new string('c', 64),
            FamilyId = Guid.CreateVersion7(),
            ExpiresAt = now.AddDays(30),
        };

        Assert.True(token.IsActive(now));

        token.RevokedAt = now;
        Assert.False(token.IsActive(now));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task An_expired_token_is_not_active()
    {
        var now = DateTimeOffset.UtcNow;
        var token = new RefreshToken
        {
            UserId = Guid.CreateVersion7(),
            TokenHash = new string('d', 64),
            FamilyId = Guid.CreateVersion7(),
            ExpiresAt = now.AddSeconds(-1),
        };

        Assert.False(token.IsActive(now));
        await Task.CompletedTask;
    }
}
