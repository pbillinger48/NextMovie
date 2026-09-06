using System.Text.Json.Nodes;

namespace NextMovie.Api.Tests.Features.Auth;

/// <summary>Helpers for comparing ProblemDetails responses.</summary>
internal static class ProblemBody
{
    /// <summary>
    /// Reads a ProblemDetails body with <c>traceId</c> stripped.
    /// </summary>
    /// <remarks>
    /// Several auth endpoints must answer different failures identically, so
    /// their responses are compared whole. Every response carries a distinct
    /// trace id derived from the request rather than from anything about the
    /// account or token, so it is the one field allowed to differ.
    /// </remarks>
    public static async Task<string> WithoutTraceIdAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
        problem.Remove("traceId");

        return problem.ToJsonString();
    }
}
