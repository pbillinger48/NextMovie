using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace NextMovie.Api.Infrastructure.OpenApi;

/// <summary>
/// Removes the spurious <c>string</c> member from numeric schema types.
/// </summary>
/// <remarks>
/// ASP.NET Core emits numeric properties as <c>["integer", "string"]</c> because
/// System.Text.Json <em>can</em> be configured to read numbers from strings. We
/// do not configure that, and our responses always serialise numbers as numbers.
/// <para>
/// Left alone, the generated TypeScript becomes <c>tmdbId: number | string</c>,
/// forcing every consumer to narrow a case that cannot occur — which is exactly
/// the loss of type fidelity ADR-0002 exists to prevent.
/// </para>
/// </remarks>
internal sealed class NumericTypeSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (schema.Type is { } type
            && (type.HasFlag(JsonSchemaType.Integer) || type.HasFlag(JsonSchemaType.Number))
            && type.HasFlag(JsonSchemaType.String))
        {
            // Clear only String; Null must survive so nullable numerics stay
            // nullable in the generated types.
            schema.Type = type & ~JsonSchemaType.String;
        }

        return Task.CompletedTask;
    }
}
