namespace NextMovie.Api.Features.Streaming;

/// <summary>Where a film can be watched, for the person asking.</summary>
/// <remarks>
/// Shared by the film page and by recommendations, unlike most response shapes in
/// this codebase. The two are genuinely the same question — "where can I watch
/// this, given who I am" — and a change to how availability is described should
/// reach both at once. Contrast <c>AuthenticatedUser</c> and
/// <c>UserProfileResponse</c>, which look alike and answer different questions,
/// and so are deliberately separate.
/// </remarks>
/// <remarks>
/// Availability is ranked on rather than filtered by (ADR-0010), so this appears
/// on every recommendation including the ones that cannot be streamed — a great
/// film you would have to rent is still worth knowing about.
/// </remarks>
/// <param name="StreamingOn">Services the user subscribes to that carry it. Press play.</param>
/// <param name="StreamingElsewhere">Services carrying it that the user has not said they have.</param>
/// <param name="RentOrBuy">Services offering it for money on top. Never described as streaming.</param>
/// <param name="CanStreamNow">Whether it can be watched without paying again.</param>
/// <param name="Known">
/// Whether availability has been looked up. False means unknown rather than
/// unavailable, and clients must say so — TMDb's provider data is not exhaustive,
/// and "we do not know" is not "it is nowhere".
/// </param>
/// <param name="Link">Where to send the viewer, when the source suggests somewhere.</param>
public sealed record WatchingOptions(
    IReadOnlyList<string> StreamingOn,
    IReadOnlyList<string> StreamingElsewhere,
    IReadOnlyList<string> RentOrBuy,
    bool CanStreamNow,
    bool Known,
    string? Link);