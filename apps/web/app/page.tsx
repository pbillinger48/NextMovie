import { Suspense } from "react";
import { MovieCard } from "./components/MovieCard";
import { SearchForm } from "./components/SearchForm";
import { searchMovies } from "@/lib/api";

type SearchPageProps = {
  // In the App Router, searchParams is a Promise and must be awaited.
  searchParams: Promise<{ title?: string | string[]; page?: string }>;
};

export default async function SearchPage({ searchParams }: SearchPageProps) {
  const params = await searchParams;

  // A repeated query string yields an array; take the first rather than
  // rendering "[object Object]" into the input.
  const rawTitle = Array.isArray(params.title) ? params.title[0] : params.title;
  const title = rawTitle?.trim() ?? "";

  const parsedPage = Number.parseInt(params.page ?? "1", 10);
  const page = Number.isSafeInteger(parsedPage) && parsedPage > 0 ? parsedPage : 1;

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">Find a film</h1>
        <p className="text-sm text-neutral-600 dark:text-neutral-400">
          Search by title. Results come from TMDb and are added to the NextMovie
          catalogue as you search.
        </p>
      </div>

      <SearchForm defaultValue={title} />

      {title === "" ? (
        <EmptyState />
      ) : (
        // key ensures a new Suspense boundary per query, so the fallback shows
        // again on each search rather than holding the previous results.
        <Suspense key={`${title}:${page}`} fallback={<ResultsSkeleton />}>
          <Results title={title} page={page} />
        </Suspense>
      )}
    </div>
  );
}

async function Results({ title, page }: { title: string; page: number }) {
  const result = await searchMovies(title, page);

  if (!result.ok) {
    return (
      <p
        role="alert"
        className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
      >
        {result.error.message}
      </p>
    );
  }

  const { results, totalResults } = result.data;

  if (results.length === 0) {
    return (
      <p className="text-sm text-neutral-600 dark:text-neutral-400">
        No films found for <span className="font-medium">{title}</span>. Try a
        different spelling or a shorter title.
      </p>
    );
  }

  return (
    <section aria-label="Search results" className="flex flex-col gap-4">
      <p aria-live="polite" className="text-sm text-neutral-600 dark:text-neutral-400">
        {totalResults.toLocaleString()}{" "}
        {totalResults === 1 ? "film" : "films"} found
      </p>

      <ul className="grid grid-cols-2 gap-x-4 gap-y-6 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
        {results.map((movie) => (
          <MovieCard key={movie.id} movie={movie} />
        ))}
      </ul>
    </section>
  );
}

function EmptyState() {
  return (
    <p className="text-sm text-neutral-600 dark:text-neutral-400">
      Enter a title above to search.
    </p>
  );
}

function ResultsSkeleton() {
  return (
    <div
      aria-hidden="true"
      className="grid grid-cols-2 gap-x-4 gap-y-6 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5"
    >
      {Array.from({ length: 10 }, (_, i) => (
        <div key={i} className="flex flex-col gap-2">
          <div className="aspect-[2/3] animate-pulse rounded-md bg-neutral-200 dark:bg-neutral-800" />
          <div className="h-4 w-3/4 animate-pulse rounded bg-neutral-200 dark:bg-neutral-800" />
          <div className="h-3 w-1/2 animate-pulse rounded bg-neutral-200 dark:bg-neutral-800" />
        </div>
      ))}
    </div>
  );
}
