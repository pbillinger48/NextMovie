using System.Net;

namespace NextMovie.Api.Infrastructure.Tmdb;

/// <summary>
/// Raised when TMDb cannot be reached or answers with a failure.
/// </summary>
/// <remarks>
/// A distinct exception type so the API can answer <c>502 Bad Gateway</c> rather
/// than <c>500</c>: the fault is an upstream dependency's, and conflating the two
/// makes real defects in our own code impossible to find in the logs.
/// </remarks>
public sealed class TmdbException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>The status TMDb returned, or null when the request never completed.</summary>
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
