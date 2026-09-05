import Image from "next/image";
import type { MovieSummary } from "@nextmovie/api-client";

/**
 * TMDb returns only relative poster paths, so the size segment is chosen here.
 * w342 is the smallest width that still looks sharp at this grid size on a 2x
 * display; next/image resizes further per breakpoint from there.
 */
const POSTER_BASE_URL = "https://image.tmdb.org/t/p/w342";

/** Widths the grid actually renders at, so the browser fetches the right size. */
const POSTER_SIZES =
  "(min-width: 1024px) 20vw, (min-width: 768px) 25vw, (min-width: 640px) 33vw, 50vw";

export function MovieCard({ movie }: { movie: MovieSummary }) {
  const year = movie.releaseDate?.slice(0, 4);

  return (
    <li className="flex flex-col gap-2">
      <div className="relative aspect-[2/3] overflow-hidden rounded-md bg-neutral-200 dark:bg-neutral-800">
        {movie.posterPath ? (
          <Image
            src={`${POSTER_BASE_URL}${movie.posterPath}`}
            alt={`Poster for ${movie.title}`}
            fill
            sizes={POSTER_SIZES}
            className="object-cover"
          />
        ) : (
          // Not every film has a poster. An explicit placeholder beats a broken
          // image icon. Hidden from assistive tech because the heading below
          // already names the film — announcing "No poster" adds noise, not
          // information.
          <div
            aria-hidden="true"
            className="flex h-full w-full items-center justify-center p-2 text-center text-xs text-neutral-500 dark:text-neutral-400"
          >
            No poster
          </div>
        )}
      </div>

      <div className="flex flex-col gap-1">
        <h3 className="text-sm leading-snug font-medium">{movie.title}</h3>

        <p className="text-xs text-neutral-600 dark:text-neutral-400">
          {year ?? "Year unknown"}
          {movie.averageRating !== null && (
            <>
              {" · "}
              <span aria-label={`Rated ${movie.averageRating.toFixed(1)} out of 10`}>
                ★ {movie.averageRating.toFixed(1)}
              </span>
            </>
          )}
        </p>

        {movie.genres.length > 0 && (
          <p className="text-xs text-neutral-500 dark:text-neutral-500">
            {movie.genres.join(", ")}
          </p>
        )}
      </div>
    </li>
  );
}
