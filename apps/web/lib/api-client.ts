import { createApiClient } from "@nextmovie/api-client";

/**
 * The single typed client for the NextMovie API.
 *
 * Shared so that the base URL is defined once. Two modules each reading the
 * environment variable is two places for them to disagree.
 *
 * Deliberately no `server-only` marker here, unlike the modules that use it.
 * `proxy.ts` refreshes tokens and is not a React Server Component context, so a
 * `server-only` import in its dependency chain would throw at runtime. The
 * browser boundary is still enforced where it belongs — on the modules that
 * touch the session cookie.
 */
export const apiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5080";

export const api = createApiClient(apiBaseUrl);
