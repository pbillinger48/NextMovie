using NextMovie.Api.Domain;
using NextMovie.Api.Domain.Authentication;

namespace NextMovie.Api.Tests.Domain;

/// <summary>
/// Tests the lockout state machine on <see cref="User"/>.
/// </summary>
/// <remarks>
/// This is the counter that decides whether an attacker gets unlimited password
/// guesses, so its edges are worth pinning down: one attempt too many and it
/// locks people out of their own accounts, one too few and it stops nothing.
/// Driven with explicit timestamps rather than the clock, so "the lockout
/// expires" is a test rather than a fifteen-minute wait.
/// </remarks>
public sealed class UserLockoutTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private static User NewUser() =>
        new() { Email = "parker@example.com", DisplayName = "Parker" };

    [Fact]
    public void A_new_user_is_not_locked_out()
    {
        Assert.False(NewUser().IsLockedOut(Now));
    }

    [Fact]
    public void Failures_below_the_limit_do_not_lock_the_account()
    {
        var user = NewUser();

        for (var attempt = 1; attempt < AuthenticationPolicy.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
            Assert.False(user.IsLockedOut(Now));
        }

        Assert.Equal(AuthenticationPolicy.MaxFailedLoginAttempts - 1, user.FailedLoginAttempts);
    }

    [Fact]
    public void The_attempt_at_the_limit_locks_the_account()
    {
        var user = FailUpToTheLimit();

        Assert.True(user.IsLockedOut(Now));
        Assert.Equal(Now + AuthenticationPolicy.LockoutDuration, user.LockoutEndsAt);
    }

    [Fact]
    public void The_lockout_lapses_on_its_own()
    {
        var user = FailUpToTheLimit();

        // No job clears this, so if it did not lapse by simple comparison the
        // account would be locked permanently.
        Assert.True(user.IsLockedOut(Now + AuthenticationPolicy.LockoutDuration - TimeSpan.FromSeconds(1)));
        Assert.False(user.IsLockedOut(Now + AuthenticationPolicy.LockoutDuration + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Locking_resets_the_counter_so_the_next_window_starts_clean()
    {
        var user = FailUpToTheLimit();

        // Otherwise a single wrong password after the lockout lapsed would
        // immediately re-lock the account, which punishes the owner far more
        // than the attacker.
        Assert.Equal(0, user.FailedLoginAttempts);
    }

    [Fact]
    public void A_successful_login_clears_failure_state()
    {
        var user = NewUser();
        user.RecordFailedLogin(Now);
        user.RecordFailedLogin(Now);

        user.RecordSuccessfulLogin(Now);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockoutEndsAt);
        Assert.Equal(Now, user.LastLoginAt);
    }

    [Fact]
    public void A_successful_login_after_a_lockout_lapses_unlocks_the_account()
    {
        var user = FailUpToTheLimit();
        var later = Now + AuthenticationPolicy.LockoutDuration + TimeSpan.FromMinutes(1);

        user.RecordSuccessfulLogin(later);

        // The stale lockout timestamp must be cleared, not merely in the past —
        // a later clock adjustment should not be able to resurrect it.
        Assert.Null(user.LockoutEndsAt);
        Assert.False(user.IsLockedOut(later));
    }

    private static User FailUpToTheLimit()
    {
        var user = NewUser();

        for (var attempt = 0; attempt < AuthenticationPolicy.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        return user;
    }
}
