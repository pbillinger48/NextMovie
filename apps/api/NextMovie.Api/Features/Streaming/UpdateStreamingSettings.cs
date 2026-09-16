using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using NextMovie.Api.Domain.Streaming;
using NextMovie.Api.Infrastructure.Auth;
using NextMovie.Api.Infrastructure.Persistence;

namespace NextMovie.Api.Features.Streaming;

/// <summary>
/// Replaces where the signed-in user watches and what they subscribe to.
/// </summary>
/// <remarks>
/// <c>PUT</c>, so the body is the whole state: the services named are the ones
/// they have, and everything else is cancelled. Unticking is how somebody says
/// they dropped a service, which a <c>POST</c> of additions could never express.
/// <para>
/// Region and services move together in one request because they are one
/// decision. Saving them separately would leave a window where the region says
/// Britain and the services are the American ones, and recommendations made in
/// that window would be confidently wrong.
/// </para>
/// </remarks>
public static class UpdateStreamingSettings
{
    /// <summary>
    /// Most services anyone will claim.
    /// </summary>
    /// <remarks>
    /// Not a product rule — nobody subscribes to fifty things — but a bound on what
    /// one request can make the database do. Without it a caller can post a
    /// hundred thousand ids and have us dutifully reconcile them.
    /// </remarks>
    private const int MaxServices = 50;

    /// <summary>Registers the streaming settings update endpoint.</summary>
    public static IEndpointRouteBuilder Map(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/users/me/streaming", HandleAsync)
            .RequireAuthorization()
            .WithName(nameof(UpdateStreamingSettings))
            .WithSummary("Update the signed-in user's streaming settings")
            .WithDescription(
                "Replaces the region and the set of subscribed services. Services absent "
                + "from the request are treated as cancelled.")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<StreamingSettingsResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        UpdateStreamingSettingsRequest request,
        ClaimsPrincipal caller,
        NextMovieDbContext db,
        ServiceCatalog catalog,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (caller.GetUserId() is not { } userId)
        {
            return StreamingSettingsResults.NoLongerSignedIn();
        }

        var wanted = (request.ProviderIds ?? []).Distinct().ToList();

        if (Validate(request, wanted) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var user = await db.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return StreamingSettingsResults.NoLongerSignedIn();
        }

        var region = request.Region!.ToUpperInvariant();

        // Warmed before the ids are checked, not after. Services are only known
        // to us once a catalogue has mentioned them, so validating first would
        // refuse a perfectly real service to any client that saves before it
        // reads — and leave whether a save works depending on what happened to
        // have been fetched earlier.
        await catalog.ForRegionAsync(region, cancellationToken);

        // Checked against what we know rather than accepted on trust: the foreign
        // key would reject an unknown id anyway, but as a 500 the caller cannot
        // act on instead of a 400 that names the problem.
        var recognised = await db.StreamingProviders
            .Where(provider => wanted.Contains(provider.Id))
            .Select(provider => provider.Id)
            .ToListAsync(cancellationToken);

        if (wanted.Except(recognised).ToList() is { Count: > 0 } unknown)
        {
            // Nothing has been written yet, so a rejected request leaves the
            // account exactly as it was rather than half-moved.
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProviderIds)] =
                    [$"Unknown services: {string.Join(", ", unknown.Order())}."],
            });
        }

        user.Region = region;
        user.UpdatedAt = time.GetUtcNow();

        await ReconcileAsync(db, userId, wanted, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(await StreamingSettings.ForAsync(user, catalog, db, cancellationToken));
    }

    /// <summary>
    /// Brings the stored subscriptions in line with the request.
    /// </summary>
    /// <remarks>
    /// A difference rather than a delete-and-reinsert, so that <c>AddedAt</c>
    /// survives on services somebody has kept. Rewriting every row on every save
    /// would make "since when" mean "since you last opened settings".
    /// </remarks>
    private static async Task ReconcileAsync(
        NextMovieDbContext db,
        Guid userId,
        IReadOnlyList<int> wanted,
        CancellationToken cancellationToken)
    {
        var existing = await db.UserStreamingProviders
            .Where(subscription => subscription.UserId == userId)
            .ToListAsync(cancellationToken);

        var keep = wanted.ToHashSet();

        db.UserStreamingProviders.RemoveRange(
            existing.Where(subscription => !keep.Contains(subscription.StreamingProviderId)));

        var held = existing.Select(subscription => subscription.StreamingProviderId).ToHashSet();

        foreach (var providerId in wanted.Where(id => !held.Contains(id)))
        {
            db.UserStreamingProviders.Add(new UserStreamingProvider
            {
                UserId = userId,
                StreamingProviderId = providerId,
            });
        }
    }

    private static Dictionary<string, string[]> Validate(
        UpdateStreamingSettingsRequest request,
        IReadOnlyList<int> providerIds)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Region))
        {
            errors[nameof(request.Region)] = ["A region is required."];
        }
        else if (!Countries.IsKnown(request.Region.Trim()))
        {
            errors[nameof(request.Region)] =
                ["A region must be an ISO 3166-1 alpha-2 country code, such as US or GB."];
        }

        if (providerIds.Count > MaxServices)
        {
            errors[nameof(request.ProviderIds)] =
                [$"At most {MaxServices} services may be selected."];
        }

        return errors;
    }
}

/// <summary>The new state of someone's streaming settings.</summary>
/// <param name="Region">ISO 3166-1 alpha-2 country code. Required.</param>
/// <param name="ProviderIds">
/// Every service they subscribe to. Omit or send an empty list to say they have
/// none — this replaces the set rather than adding to it.
/// </param>
public sealed record UpdateStreamingSettingsRequest(string? Region, IReadOnlyList<int>? ProviderIds);
