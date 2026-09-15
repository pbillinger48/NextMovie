import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import { redirect } from "next/navigation";

import { getRecommendations, type RecommendedFilm } from "@/lib/recommendations-api";
import { formatRuntime } from "@/lib/format";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "What to watch · NextMovie" };

const POSTER_BASE_URL = "https://image.tmdb.org/t/p/w185";

const COUNT = 12;

export default async function RecommendationsPage() {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const result = await getRecommendations(session.accessToken, COUNT);

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

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">What to watch</h1>
        <p className="max-w-prose text-sm text-neutral-600 dark:text-neutral-400">
          Films you haven&apos;t seen, chosen from what you watch and rate. Each one
          says why it is here.
        </p>
      </div>

      {result.films.length === 0 ? <NothingYet /> : <Films films={result.films} />}

      {/*
        ADR-0008 keeps streaming availability out of the first version, so the
        page says so rather than letting the omission read as an oversight.
      */}
      <p className="max-w-prose border-t border-neutral-200 pt-4 text-xs text-neutral-500 dark:border-neutral-800">
        NextMovie cannot yet tell you where to stream these. That is coming; until
        then these are what to watch, not where.
      </p>
    </div>
  );
}

function Films({ films }: { films: RecommendedFilm[] }) {
  return (
    <ol className="flex flex-col gap-4">
      {films.map((film) => (
        <li key={film.movieId}>
          <Film film={film} />
        </li>
      ))}
    </ol>
  );
}

function Film({ film }: { film: RecommendedFilm }) {
  const year = film.releaseDate?.slice(0, 4);

  return (
    <Link
      href={`/movies/${film.movieId}`}
      className="group flex gap-4 rounded-lg border border-neutral-200 p-3 hover:border-blue-600 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-800"
    >
      <div className="relative aspect-[2/3] w-20 shrink-0 overflow-hidden rounded bg-neutral-200 sm:w-24 dark:bg-neutral-800">
        {film.posterPath ? (
          <Image
            src={`${POSTER_BASE_URL}${film.posterPath}`}
            alt=""
            aria-hidden="true"
            fill
            sizes="6rem"
            className="object-cover"
          />
        ) : null}
      </div>

      <div className="flex flex-col gap-1.5">
        <h2 className="leading-snug font-medium group-hover:underline underline-offset-2">
          {film.title}
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

        {film.reasons.length > 0 ? (
          <ul className="flex flex-col gap-0.5 text-xs text-neutral-700 dark:text-neutral-300">
            {film.reasons.map((reason) => (
              <li key={reason}>· {reason}</li>
            ))}
          </ul>
        ) : null}

        {/*
          Only shown when it is not high. Announcing "High confidence" on every
          row is noise; saying so when it is low is information.
        */}
        {film.confidence !== "High" ? (
          <p className="text-xs text-amber-700 dark:text-amber-500">
            {film.confidence === "Low"
              ? "A guess — we don't know much about films like this from you yet"
              : "A reasonable guess, from limited history"}
          </p>
        ) : null}
      </div>
    </Link>
  );
}

/**
 * What someone sees before they have rated anything.
 *
 * The API returns an empty list rather than falling back to popular films, so
 * this has to explain rather than apologise — and point at the two things that
 * actually fix it.
 */
function NothingYet() {
  return (
    <div className="flex max-w-prose flex-col items-start gap-4 rounded-lg border border-neutral-200 p-6 dark:border-neutral-800">
      <h2 className="font-medium">Nothing to suggest yet</h2>

      <p className="text-sm text-neutral-600 dark:text-neutral-400">
        Recommendations are built from films you have rated highly. Rate a few you
        love — or bring your history across from Letterboxd — and they will appear
        here.
      </p>

      <div className="flex flex-wrap gap-3">
        <Link
          href="/"
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
        >
          Find films to rate
        </Link>
        <Link
          href="/import"
          className="rounded-md border border-neutral-300 px-4 py-2 text-sm font-medium hover:bg-neutral-50 dark:border-neutral-700 dark:hover:bg-neutral-800"
        >
          Import from Letterboxd
        </Link>
      </div>
    </div>
  );
}
