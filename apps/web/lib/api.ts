/**
 * API access for server components.
 *
 * The `server-only` import below is load-bearing: it makes importing this module
 * from a client component a build error rather than a runtime surprise. Per
 * ADR-0001 the browser never talks to the API directly — that is what keeps CORS
 * unnecessary and leaves the BFF option open.
 */
import "server-only";

import { type MovieDetails, type SearchMoviesResponse } from "@nextmovie/api-client";

import { api } from "./api-client";

/** A search that failed in a way worth showing the user. */
export type SearchFailure =
  | { kind: "validation"; message: string }
  | { kind: "upstream"; message: string }
  | { kind: "unreachable"; message: string };

export type SearchResult =
  | { ok: true; data: SearchMoviesResponse }
  | { ok: false; error: SearchFailure };

/**
 * Searches films by title.
 *
 * Returns a discriminated result rather than throwing. A failed search is an
 * ordinary outcome for this page — TMDb can be down, the query can be invalid —
 * and modelling it in the return type forces the page to render a real state for
 * each case instead of falling through to a generic error boundary.
 */
export async function searchMovies(
  title: string,
  page: number = 1,
): Promise<SearchResult> {
  try {
    const { data, error, response } = await api.GET("/api/v1/movies/search", {
      params: { query: { title, page } },
    });

    if (data) {
      return { ok: true, data };
    }

    if (response.status === 400) {
      const detail =
        error && typeof error === "object" && "errors" in error
          ? Object.values(error.errors ?? {}).flat().join(" ")
          : "That search could not be understood.";

      return { ok: false, error: { kind: "validation", message: detail } };
    }

    return {
      ok: false,
      error: {
        kind: "upstream",
        message: "The movie database is unavailable right now. Please try again shortly.",
      },
    };
  } catch {
    // The API itself is unreachable — a different failure from TMDb being down,
    // and worth telling the user apart from it.
    return {
      ok: false,
      error: {
        kind: "unreachable",
        message: "Could not reach NextMovie. Please try again shortly.",
      },
    };
  }
}

/** A film that could not be shown, and why. */
export type MovieFailure =
  | { kind: "not-found" }
  | { kind: "unreachable"; message: string };

export type MovieResult =
  | { ok: true; data: MovieDetails }
  | { ok: false; error: MovieFailure };

/**
 * Fetches one film by its NextMovie identifier.
 *
 * "Not in the catalogue" is separated from "could not ask" deliberately: the
 * first is a 404 page, the second is a temporary problem the visitor should be
 * invited to retry. Collapsing them would tell someone a film does not exist
 * because our API blinked.
 */
export async function getMovie(id: string): Promise<MovieResult> {
  try {
    const { data, response } = await api.GET("/api/v1/movies/{id}", {
      params: { path: { id } },
    });

    if (data) {
      return { ok: true, data };
    }

    if (response.status === 404) {
      return { ok: false, error: { kind: "not-found" } };
    }

    return {
      ok: false,
      error: {
        kind: "unreachable",
        message: "That film could not be loaded right now. Please try again shortly.",
      },
    };
  } catch {
    return {
      ok: false,
      error: {
        kind: "unreachable",
        message: "Could not reach NextMovie. Please try again shortly.",
      },
    };
  }
}
