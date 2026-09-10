using System.Net.Http.Json;
using System.Text.Json;
using NextMovie.Api.Infrastructure.Tmdb.Dtos;

namespace NextMovie.Api.Infrastructure.Tmdb;

/// <summary>Typed HTTP client for TMDb.</summary>
/// <remarks>
/// Authentication, base address and the resilience pipeline are configured on the
/// registration in <c>Program.cs</c>, not here — this type is only responsible
/// for shaping requests and translating failures.
/// </remarks>
internal sealed class TmdbClient(HttpClient httpClient, ILogger<TmdbClient> logger) : ITmdbClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // TMDb uses snake_case throughout. One policy here beats a
        // [JsonPropertyName] attribute on every property of every DTO.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    public Task<TmdbSearchResponse> SearchMoviesAsync(
        string title,
        int page,
        CancellationToken cancellationToken)
    {
        // Escaped, not interpolated raw: titles legitimately contain &, ? and #.
        var requestUri =
            $"search/movie?query={Uri.EscapeDataString(title)}&page={page}&include_adult=false";

        return SendAsync<TmdbSearchResponse>(
            requestUri,
            () => new TmdbSearchResponse(),
            $"search for {title} page {page}",
            cancellationToken);
    }

    public Task<TmdbMovieDetailsResponse> GetMovieAsync(int tmdbId, CancellationToken cancellationToken) =>
        SendAsync<TmdbMovieDetailsResponse>(
            $"movie/{tmdbId}",

            // An empty details response would look like a film with no title,
            // which the mapper rejects — better than pretending we fetched
            // something. A 200 with no body should not happen; if it does, the
            // caller keeps the data it already had.
            () => new TmdbMovieDetailsResponse(),
            $"details for film {tmdbId}",
            cancellationToken);

    /// <summary>
    /// Issues a GET and translates every failure mode into <see cref="TmdbException"/>.
    /// </summary>
    /// <remarks>
    /// Shared because the failure handling is the valuable part and is identical
    /// for every endpoint: an unreachable host, a pipeline timeout, an upstream
    /// status, and a success carrying unparseable JSON each mean something
    /// different and each must not surface as a raw HTTP exception.
    /// </remarks>
    private async Task<TResponse> SendAsync<TResponse>(
        string requestUri,
        Func<TResponse> emptyResponse,
        string description,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;

        try
        {
            response = await httpClient.GetAsync(requestUri, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TmdbException("TMDb could not be reached.", statusCode: null, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancellation not requested by the caller means the resilience
            // pipeline's timeout elapsed, not a client disconnect.
            throw new TmdbException("The request to TMDb timed out.", statusCode: null, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Logged at warning, not error: an upstream 4xx/5xx is expected
                // operational noise, and paging on it would be wrong.
                logger.LogWarning(
                    "TMDb request failed with {StatusCode} for {Description}",
                    (int)response.StatusCode,
                    description);

                throw new TmdbException(
                    $"TMDb returned {(int)response.StatusCode}.",
                    response.StatusCode);
            }

            try
            {
                var payload = await response.Content.ReadFromJsonAsync<TResponse>(
                    SerializerOptions,
                    cancellationToken);

                return payload ?? emptyResponse();
            }
            catch (JsonException ex)
            {
                // A success status carrying unparseable JSON means TMDb changed
                // its contract. Treat it as an upstream fault, not our bug.
                throw new TmdbException("TMDb returned a response that could not be parsed.", response.StatusCode, ex);
            }
        }
    }
}
