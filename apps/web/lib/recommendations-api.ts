import "server-only";

import type { RecommendedFilm } from "@nextmovie/api-client";

import { api } from "./api-client";

export type { RecommendedFilm };

export type RecommendationsResult =
  | { ok: true; films: RecommendedFilm[] }
  | { ok: false; message: string };

/**
 * Asks the API what to watch next.
 *
 * An empty list is a success, not a failure: it means there is not enough history
 * to reason from, and the page says so rather than showing an error.
 */
export async function getRecommendations(
  accessToken: string,
  count: number,
): Promise<RecommendationsResult> {
  try {
    const { data } = await api.GET("/api/v1/recommendations", {
      params: { query: { count } },
      headers: { Authorization: `Bearer ${accessToken}` },

      // Each request asks TMDb fresh, and a cached recommendation list is a
      // stale one — the point is what to watch now.
      cache: "no-store",
    });

    return data
      ? { ok: true, films: data.recommendations }
      : { ok: false, message: "Recommendations could not be loaded right now." };
  } catch {
    return { ok: false, message: "Could not reach NextMovie. Please try again shortly." };
  }
}
