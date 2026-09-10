import type { Metadata } from "next";
import Image from "next/image";
import { notFound } from "next/navigation";

import { getMovie } from "@/lib/api";
import { formatRuntime } from "@/lib/format";

/**
 * A single film.
 *
 * Larger poster than the search grid, and a backdrop, so the size segments differ
 * from MovieCard's — TMDb serves fixed widths and asking for the wrong one either
 * looks soft or wastes bandwidth.
 */
const POSTER_BASE_URL = "https://image.tmdb.org/t/p/w500";
const BACKDROP_BASE_URL = "https://image.tmdb.org/t/p/w1280";

type MoviePageProps = {
  // Route params are a Promise in the App Router and must be awaited.
  params: Promise<{ id: string }>;
};

export async function generateMetadata({ params }: MoviePageProps): Promise<Metadata> {
  const { id } = await params;
  const result = await getMovie(id);

  if (!result.ok) {
    return { title: "Film · NextMovie" };
  }

  const year = result.data.releaseDate?.slice(0, 4);

  return {
    title: `${result.data.title}${year ? ` (${year})` : ""} · NextMovie`,
    description: result.data.overview ?? undefined,
  };
}

export default async function MoviePage({ params }: MoviePageProps) {
  const { id } = await params;
  const result = await getMovie(id);

  if (!result.ok) {
    if (result.error.kind === "not-found") {
      // A real 404, rendered by the not-found boundary with the right status
      // code — a film we do not have should not be a soft 200 saying "sorry".
      notFound();
    }

    return (
      <p
        role="alert"
        className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
      >
        {result.error.message}
      </p>
    );
  }

  const movie = result.data;
  const year = movie.releaseDate?.slice(0, 4);

  return (
    <article className="flex flex-col gap-8">
      {movie.backdropPath ? (
        <div className="relative -mx-4 aspect-[16/7] overflow-hidden sm:mx-0 sm:rounded-lg">
          <Image
            src={`${BACKDROP_BASE_URL}${movie.backdropPath}`}
            alt=""
            fill
            // Decorative: the title is right below it as real text, so
            // describing it again would be noise for a screen reader.
            aria-hidden="true"
            sizes="(min-width: 1024px) 64rem, 100vw"
            className="object-cover"
            priority
          />
        </div>
      ) : null}

      <div className="flex flex-col gap-6 sm:flex-row sm:gap-8">
        <div className="relative aspect-[2/3] w-40 shrink-0 overflow-hidden rounded-md bg-neutral-200 sm:w-56 dark:bg-neutral-800">
          {movie.posterPath ? (
            <Image
              src={`${POSTER_BASE_URL}${movie.posterPath}`}
              alt={`Poster for ${movie.title}`}
              fill
              sizes="(min-width: 640px) 14rem, 10rem"
              className="object-cover"
            />
          ) : (
            <div
              aria-hidden="true"
              className="flex h-full w-full items-center justify-center p-2 text-center text-xs text-neutral-500 dark:text-neutral-400"
            >
              No poster
            </div>
          )}
        </div>

        <div className="flex flex-col gap-4">
          <div className="flex flex-col gap-1">
            <h1 className="text-3xl font-semibold tracking-tight">{movie.title}</h1>

            {movie.originalTitle && movie.originalTitle !== movie.title ? (
              <p className="text-sm text-neutral-600 dark:text-neutral-400">
                Originally <span lang={movie.language ?? undefined}>{movie.originalTitle}</span>
              </p>
            ) : null}
          </div>

          <Facts movie={movie} year={year} />

          {movie.genres.length > 0 ? (
            <ul className="flex flex-wrap gap-2">
              {movie.genres.map((genre) => (
                <li
                  key={genre}
                  className="rounded-full border border-neutral-300 px-3 py-1 text-xs dark:border-neutral-700"
                >
                  {genre}
                </li>
              ))}
            </ul>
          ) : null}

          {movie.overview ? (
            <p className="max-w-prose leading-relaxed text-neutral-800 dark:text-neutral-200">
              {movie.overview}
            </p>
          ) : (
            <p className="text-sm text-neutral-600 dark:text-neutral-400">
              No synopsis available for this film yet.
            </p>
          )}
        </div>
      </div>
    </article>
  );
}

function Facts({
  movie,
  year,
}: {
  movie: { runtime: number | null; averageRating: number | null; status: string | null };
  year: string | undefined;
}) {
  const facts = [
    year ?? "Year unknown",
    movie.runtime !== null ? formatRuntime(movie.runtime) : null,

    // Absent rather than zero: the API returns null for a film nobody has rated,
    // and showing "0.0" would call it terrible rather than unrated.
    movie.averageRating !== null ? `★ ${movie.averageRating.toFixed(1)}` : null,

    // Only worth saying when it is not the ordinary case.
    movie.status !== null && movie.status !== "Released" ? movie.status : null,
  ].filter((fact): fact is string => fact !== null);

  return (
    <p className="text-sm text-neutral-600 dark:text-neutral-400">{facts.join(" · ")}</p>
  );
}
