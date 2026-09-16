import "server-only";

import type { MovieResponseState, SavedFilm } from "@nextmovie/api-client";

import { api } from "./api-client";

/**
 * What the signed-in user decided to do about a film, and the list of what they
 * saved.
 *
 * One module because they are one feature: the watchlist *is* the set of films
 * answered `Saved` (ADR-0011), so splitting the read from the writes that fill it
 * would put two halves of one idea in two places.
 */

export type { MovieResponseState, SavedFilm };

/** The three answers the API understands. */
export type MovieResponse = "Saved" | "NotInterested" | "Seen";

export type WatchlistResult =
  | { ok: true; films: SavedFilm[] }
  | { ok: false; message: string };

/**
 * Records an answer about a film.
 *
 * Returns whether it was saved. Callers are page actions with nothing useful to
 * do about a failure beyond re-rendering, and the button state comes from the
 * next server render either way.
 */
export async function respondToMovie(
  accessToken: string,
  movieId: string,
  response: MovieResponse,
): Promise<boolean> {
  try {
    const { data } = await api.PUT("/api/v1/movies/{id}/response", {
      params: { path: { id: movieId } },
      headers: { Authorization: `Bearer ${accessToken}` },
      body: { response },
    });

    return data !== undefined;
  } catch {
    return false;
  }
}

/** Withdraws an answer, taking a film off the watchlist or un-hiding it. */
export async function withdrawResponse(
  accessToken: string,
  movieId: string,
): Promise<boolean> {
  try {
    const { data } = await api.DELETE("/api/v1/movies/{id}/response", {
      params: { path: { id: movieId } },
      headers: { Authorization: `Bearer ${accessToken}` },
    });

    return data !== undefined;
  } catch {
    return false;
  }
}

/**
 * Reads the films the user saved.
 *
 * An empty list is a success. Somebody who has saved nothing has an empty
 * watchlist, which is a state to explain rather than an error to report.
 */
export async function fetchWatchlist(accessToken: string): Promise<WatchlistResult> {
  try {
    const { data } = await api.GET("/api/v1/users/me/watchlist", {
      headers: { Authorization: `Bearer ${accessToken}` },

      // Availability moves, and so does the list itself the moment anything is
      // saved. A cached watchlist is a wrong one.
      cache: "no-store",
    });

    return data
      ? { ok: true, films: data.films }
      : { ok: false, message: "Your watchlist could not be loaded right now." };
  } catch {
    return { ok: false, message: "Could not reach NextMovie. Please try again shortly." };
  }
}
