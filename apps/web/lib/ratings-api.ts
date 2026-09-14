import type { MovieRating } from "@nextmovie/api-client";

import { api } from "./api-client";

/**
 * The signed-in user's own ratings.
 *
 * Native rating is a first-class writer alongside the Letterboxd import
 * (ADR-0006), not a lesser version of it — the same tables, the same rules, and
 * a rating typed here outranks one a re-import would supply.
 */

export type { MovieRating };

/** Reads the user's rating of one film, or null if they have not rated it. */
export async function getMyRating(
  accessToken: string,
  movieId: string,
): Promise<MovieRating | null> {
  try {
    const { data } = await api.GET("/api/v1/movies/{id}/rating", {
      params: { path: { id: movieId } },
      headers: { Authorization: `Bearer ${accessToken}` },
      cache: "no-store",
    });

    return data ?? null;
  } catch {
    // A film page is worth rendering without the rating widget's current value.
    // Failing the whole page because this one call did not answer would be a
    // poor trade.
    return null;
  }
}

/** Records a rating. Returns whether it was saved. */
export async function rateMovie(
  accessToken: string,
  movieId: string,
  rating: number,
): Promise<boolean> {
  try {
    const { data } = await api.PUT("/api/v1/movies/{id}/rating", {
      params: { path: { id: movieId } },
      headers: { Authorization: `Bearer ${accessToken}` },
      body: { rating },
    });

    return data !== undefined;
  } catch {
    return false;
  }
}

/** Removes a rating. */
export async function unrateMovie(accessToken: string, movieId: string): Promise<boolean> {
  try {
    const { response } = await api.DELETE("/api/v1/movies/{id}/rating", {
      params: { path: { id: movieId } },
      headers: { Authorization: `Bearer ${accessToken}` },
    });

    return response.ok;
  } catch {
    return false;
  }
}
