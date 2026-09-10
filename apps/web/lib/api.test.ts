import { afterEach, describe, expect, it, vi } from "vitest";

/**
 * How the film endpoint's outcomes are translated for the page.
 *
 * The distinction that matters is "not in the catalogue" versus "could not ask":
 * the first is a 404 page, the second is a temporary problem worth retrying.
 * Collapsing them would tell someone a film does not exist because our API
 * blinked.
 */

const movie = {
  id: "0199aaaa-bbbb-cccc-dddd-eeeeffff0000",
  tmdbId: 27205,
  title: "Inception",
  originalTitle: "Inception",
  overview: "A thief who steals corporate secrets.",
  posterPath: "/poster.jpg",
  backdropPath: "/backdrop.jpg",
  releaseDate: "2010-07-15",
  runtime: 148,
  averageRating: 8.4,
  popularity: 82.3,
  language: "en",
  status: "Released",
  genres: ["Action", "Science Fiction"],
};

/**
 * The import has to follow the stub: openapi-fetch captures `globalThis.fetch`
 * when the client is created, at module load.
 */
async function apiReturning(status: number, body: unknown) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () =>
      new Response(JSON.stringify(body), {
        status,
        headers: { "content-type": status >= 400 ? "application/problem+json" : "application/json" },
      }),
    ),
  );

  vi.resetModules();

  return import("./api");
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("getMovie", () => {
  it("returns the film", async () => {
    const { getMovie } = await apiReturning(200, movie);

    const result = await getMovie(movie.id);

    expect(result.ok).toBe(true);
    expect(result.ok && result.data.title).toBe("Inception");
    expect(result.ok && result.data.runtime).toBe(148);
  });

  it("reports a film the catalogue does not hold as not found", async () => {
    const { getMovie } = await apiReturning(404, { title: "Film not found", status: 404 });

    const result = await getMovie(movie.id);

    // The page turns this into a real 404 rather than a soft 200.
    expect(!result.ok && result.error.kind).toBe("not-found");
  });

  it("reports a server fault as temporary, not as a missing film", async () => {
    const { getMovie } = await apiReturning(500, { title: "Server error", status: 500 });

    const result = await getMovie(movie.id);

    expect(!result.ok && result.error.kind).toBe("unreachable");
  });

  it("reports an unreachable API as temporary", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => { throw new TypeError("fetch failed"); }));
    vi.resetModules();

    const { getMovie } = await import("./api");
    const result = await getMovie(movie.id);

    expect(!result.ok && result.error.kind).toBe("unreachable");
  });
});
