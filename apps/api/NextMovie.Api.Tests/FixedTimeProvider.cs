namespace NextMovie.Api.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> stopped at a chosen instant.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from <c>Microsoft.Extensions.TimeProvider.Testing</c>:
/// the tests here only ever need "what if the clock said this?", and a package
/// dependency for four lines would not pay for itself. Reach for the real fake
/// if a test ever needs timers to advance.
/// </remarks>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    /// <summary>The instant to report. Settable so a test can move time forward.</summary>
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
