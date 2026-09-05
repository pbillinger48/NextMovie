/**
 * Search input.
 *
 * A plain GET form, deliberately. Submitting navigates to `/?title=…`, which
 * makes every result set bookmarkable and shareable, keeps the page working with
 * JavaScript disabled, and needs no client-side JavaScript at all.
 */
export function SearchForm({ defaultValue }: { defaultValue?: string }) {
  return (
    <form action="/" method="get" role="search" className="flex gap-2">
      <label htmlFor="title" className="sr-only">
        Search films by title
      </label>

      <input
        id="title"
        name="title"
        type="search"
        defaultValue={defaultValue}
        placeholder="Search films by title…"
        autoComplete="off"
        // Autofocus is safe here: search is the sole purpose of this page, so
        // it does not steal focus from anything a user was already doing.
        autoFocus
        className="w-full rounded-md border border-neutral-300 bg-white px-3 py-2 text-base shadow-sm placeholder:text-neutral-500 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-700 dark:bg-neutral-900 dark:placeholder:text-neutral-400"
      />

      <button
        type="submit"
        className="rounded-md bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600"
      >
        Search
      </button>
    </form>
  );
}
