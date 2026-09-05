using Microsoft.AspNetCore.Diagnostics;
using NextMovie.Api.Infrastructure.Tmdb;

namespace NextMovie.Api.Infrastructure.ErrorHandling;

/// <summary>
/// Translates <see cref="TmdbException"/> into <c>502 Bad Gateway</c>.
/// </summary>
/// <remarks>
/// Without this, an upstream outage surfaces as <c>500</c> and is indistinguishable
/// from a defect in our own code — which makes the logs useless during exactly the
/// incident you need them for.
/// </remarks>
internal sealed class TmdbExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<TmdbExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not TmdbException tmdbException)
        {
            // Not ours: let the next handler deal with it.
            return false;
        }

        logger.LogWarning(
            tmdbException,
            "TMDb request failed with upstream status {StatusCode}",
            tmdbException.StatusCode);

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = tmdbException,
            ProblemDetails =
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Movie provider unavailable",

                // Deliberately generic. The upstream status and message are in
                // the logs; echoing them to callers leaks our dependencies and
                // their failure modes to the public internet.
                Detail = "The movie database could not be reached. Please try again shortly.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.3",
            },
        });
    }
}
