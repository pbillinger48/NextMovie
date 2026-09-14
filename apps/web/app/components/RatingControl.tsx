import Link from "next/link";

import { rate, unrate } from "@/app/actions/ratings";

/**
 * Half-star rating for a film.
 *
 * Ten buttons in ten forms rather than a JavaScript star widget. It works with
 * no client JavaScript, every value is reachable by keyboard in order, and each
 * one announces what it does — which a row of hover-sensitive half-star hotspots
 * does not.
 */
export function RatingControl({
  movieId,
  rating,
  signedIn,
}: {
  movieId: string;
  rating: number | null;
  signedIn: boolean;
}) {
  if (!signedIn) {
    return (
      <p className="text-sm text-neutral-600 dark:text-neutral-400">
        <Link href="/sign-in" className="underline underline-offset-2">
          Sign in
        </Link>{" "}
        to rate this film.
      </p>
    );
  }

  const steps = Array.from({ length: 10 }, (_, index) => (index + 1) / 2);

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-3">
        <span id="your-rating" className="text-sm font-medium">
          Your rating
        </span>

        <span className="text-sm text-neutral-600 tabular-nums dark:text-neutral-400">
          {rating === null ? "Not rated" : `★ ${rating.toFixed(1)}`}
        </span>
      </div>

      <div role="group" aria-labelledby="your-rating" className="flex flex-wrap items-center gap-1">
        {steps.map((value) => (
          <form action={rate} key={value}>
            <input type="hidden" name="movieId" value={movieId} />
            <input type="hidden" name="rating" value={value} />

            <button
              type="submit"
              // The current rating is announced above, so this says what the
              // button does rather than repeating the state.
              aria-label={`Rate ${value.toFixed(1)} out of 5`}
              aria-pressed={rating === value}
              className={`w-11 rounded-md border px-2 py-1.5 text-xs tabular-nums transition-colors ${
                rating !== null && value <= rating
                  ? "border-amber-500 bg-amber-500 text-white"
                  : "border-neutral-300 hover:border-amber-500 dark:border-neutral-700"
              }`}
            >
              {value.toFixed(1)}
            </button>
          </form>
        ))}

        {rating !== null ? (
          <form action={unrate} className="ml-2">
            <input type="hidden" name="movieId" value={movieId} />
            <button
              type="submit"
              className="rounded-md border border-neutral-300 px-3 py-1.5 text-xs hover:bg-neutral-50 dark:border-neutral-700 dark:hover:bg-neutral-800"
            >
              Clear
            </button>
          </form>
        ) : null}
      </div>
    </div>
  );
}
