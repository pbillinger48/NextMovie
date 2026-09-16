import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import { redirect } from "next/navigation";

import { ResponseButtons } from "@/app/components/ResponseButtons";
import { WatchOptions } from "@/app/components/WatchOptions";
import { formatRuntime } from "@/lib/format";
import { fetchWatchlist, type SavedFilm } from "@/lib/responses-api";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "Your watchlist · NextMovie" };

const POSTER_BASE_URL = "https://image.tmdb.org/t/p/w185";

/**
 * Films saved for later.
 *
 * Ordered newest first and led by where each can be watched, because the question
 * people open a watchlist with is "what can I watch tonight" rather than "what did
 * I mean to watch".
 */
export default async function WatchlistPage() {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const result = await fetchWatchlist(session.accessToken);

  if (!result.ok) {
    return (
      <p
        role="alert"
        className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
      >
        {result.message}
      </p>
    );
  }

  const streamable = result.films.filter((film) => film.watch.canStreamNow).length;

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">Your watchlist</h1>
        <p className="max-w-prose text-sm text-neutral-600 dark:text-neutral-400">
          {result.films.length === 0
            ? "Films you save will appear here."
            : streamable > 0
              ? `${result.films.length} saved — ${streamable} you can start right now.`
              : `${result.films.length} saved. None are on a service you subscribe to.`}
        </p>
      </div>

      {result.films.length === 0 ? (
        <NothingSaved />
      ) : (
        <ul className="flex flex-col gap-4">
          {result.films.map((film) => (
            <li key={film.movieId}>
              <Film film={film} />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function Film({ film }: { film: SavedFilm }) {
  const year = film.releaseDate?.slice(0, 4);

  return (
    <div className="flex gap-4 rounded-lg border border-neutral-200 p-3 dark:border-neutral-800">
      <Link
        href={`/movies/${film.movieId}`}
        tabIndex={-1}
        aria-hidden="true"
        className="relative aspect-[2/3] w-20 shrink-0 overflow-hidden rounded bg-neutral-200 sm:w-24 dark:bg-neutral-800"
      >
        {film.posterPath ? (
          <Image
            src={`${POSTER_BASE_URL}${film.posterPath}`}
            alt=""
            fill
            sizes="6rem"
            className="object-cover"
          />
        ) : null}
      </Link>

      <div className="flex min-w-0 flex-col gap-1.5">
        <h2 className="leading-snug font-medium">
          <Link
            href={`/movies/${film.movieId}`}
            className="hover:underline underline-offset-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600"
          >
            {film.title}
          </Link>
        </h2>

        <p className="text-xs text-neutral-600 dark:text-neutral-400">
          {[
            year,
            film.runtime !== null ? formatRuntime(film.runtime) : null,
            film.averageRating !== null ? `★ ${film.averageRating.toFixed(1)}` : null,
            film.genres.slice(0, 3).join(", ") || null,
          ]
            .filter(Boolean)
            .join(" · ")}
        </p>

        <WatchOptions watch={film.watch} />

        <div className="pt-1">
          {/* Everything here is saved by definition, so the buttons open on the
              "remove it" state rather than offering to save it again. */}
          <ResponseButtons movieId={film.movieId} response="Saved" watched={false} />
        </div>
      </div>
    </div>
  );
}

function NothingSaved() {
  return (
    <div className="flex max-w-prose flex-col items-start gap-4 rounded-lg border border-neutral-200 p-6 dark:border-neutral-800">
      <h2 className="font-medium">Nothing saved yet</h2>

      <p className="text-sm text-neutral-600 dark:text-neutral-400">
        Save a film from its page or from your recommendations, and it will wait
        here until you watch it.
      </p>

      <Link
        href="/recommendations"
        className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
      >
        See what to watch
      </Link>
    </div>
  );
}
