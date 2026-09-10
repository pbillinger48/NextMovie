import Link from "next/link";

/**
 * Rendered whenever a route calls `notFound()`, with a real 404 status.
 *
 * The film page reaches here for an identifier the catalogue does not hold —
 * a mistyped URL, or a film that was never searched for. Search is the way back
 * in, since the catalogue only contains films someone has already looked for.
 */
export default function NotFound() {
  return (
    <div className="flex flex-col items-start gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">Not found</h1>

      <p className="text-sm text-neutral-600 dark:text-neutral-400">
        We could not find that page. If you were looking for a film, try searching
        for it by title.
      </p>

      <Link
        href="/"
        className="rounded-md bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600"
      >
        Search films
      </Link>
    </div>
  );
}
