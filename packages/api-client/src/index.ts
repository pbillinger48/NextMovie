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
export type MovieDetails = components["schemas"]["MovieDetails"];
export type HealthResponse = components["schemas"]["HealthResponse"];
export type ProblemDetails = components["schemas"]["HttpValidationProblemDetails"];
export type AuthenticationResponse = components["schemas"]["AuthenticationResponse"];
export type AuthenticatedUser = components["schemas"]["AuthenticatedUser"];
export type UserProfileResponse = components["schemas"]["UserProfileResponse"];
export type ImportJobStatusResponse = components["schemas"]["ImportJobStatusResponse"];
export type ImportReviewResponse = components["schemas"]["ImportReviewResponse"];
export type ImportReviewItem = components["schemas"]["ImportReviewItem"];
export type ImportCandidate = components["schemas"]["ImportCandidate"];
export type MovieRating = components["schemas"]["MovieRating"];
export type RecommendationsResponse = components["schemas"]["RecommendationsResponse"];
export type RecommendedFilm = components["schemas"]["RecommendedFilm"];
export type StreamingSettingsResponse = components["schemas"]["StreamingSettingsResponse"];
export type StreamingServiceOption = components["schemas"]["StreamingServiceOption"];
export type CountryOption = components["schemas"]["CountryOption"];
export type MovieResponseState = components["schemas"]["MovieResponseState"];
export type WatchlistResponse = components["schemas"]["WatchlistResponse"];
export type SavedFilm = components["schemas"]["SavedFilm"];
export type WatchingOptions = components["schemas"]["WatchingOptions"];

export type { paths };

/**
 * Creates a typed client for the NextMovie API.
 *
 * @param baseUrl Absolute base URL of the API, e.g. `http://localhost:5080`.
 */
export function createApiClient(baseUrl: string) {
  return createClient<paths>({ baseUrl });
}
