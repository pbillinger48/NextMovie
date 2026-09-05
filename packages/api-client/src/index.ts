import createClient from "openapi-fetch";
import type { components, paths } from "./schema.js";

/**
 * Types generated from the API's OpenAPI schema (ADR-0002).
 *
 * Never hand-edit `schema.d.ts`, and never redeclare these shapes by hand — a
 * duplicate declaration compiles happily while diverging from what the API
 * actually returns, which is the exact failure this package exists to prevent.
 */
export type MovieSummary = components["schemas"]["MovieSummary"];
export type SearchMoviesResponse = components["schemas"]["SearchMoviesResponse"];
export type HealthResponse = components["schemas"]["HealthResponse"];
export type ProblemDetails = components["schemas"]["HttpValidationProblemDetails"];

export type { paths };

/**
 * Creates a typed client for the NextMovie API.
 *
 * @param baseUrl Absolute base URL of the API, e.g. `http://localhost:5080`.
 */
export function createApiClient(baseUrl: string) {
  return createClient<paths>({ baseUrl });
}
