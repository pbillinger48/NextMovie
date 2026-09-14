import type { Metadata } from "next";
import Image from "next/image";
import Link from "next/link";
import { notFound, redirect } from "next/navigation";

import { dismissRow, resolveRow } from "@/app/actions/import";
import { getImportReview, type ImportCandidate, type ImportReviewItem } from "@/lib/import-api";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "Review import · NextMovie" };

const POSTER_BASE_URL = "https://image.tmdb.org/t/p/w154";

type ReviewPageProps = {
  params: Promise<{ jobId: string }>;
};

/**
 * The one screen where the system admits it does not know something.
 *
 * Each row shows what Letterboxd said on the left and the films it might mean on
 * the right, because the decision is nearly always obvious once the two are side
 * by side — a real film next to an obscure short of the same name and year.
 */
export default async function ReviewPage({ params }: ReviewPageProps) {
  const { jobId } = await params;
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const result = await getImportReview(session.accessToken, jobId);

  if (!result.ok) {
    if (result.error.kind === "not-found") {
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

  const { items } = result.data;

  if (items.length === 0) {
    return (
      <div className="flex flex-col items-start gap-4">
        <h1 className="text-2xl font-semibold tracking-tight">Nothing left to review</h1>
        <p className="text-sm text-neutral-600 dark:text-neutral-400">
          Every film from that import has been dealt with.
        </p>
        <Link
          href={`/import/${jobId}`}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
        >
          Back to the import
        </Link>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          {items.length === 1 ? "One film to check" : `${items.length} films to check`}
        </h1>
        <p className="max-w-prose text-sm text-neutral-600 dark:text-neutral-400">
          These could not be identified with confidence, so nothing was imported for
          them. Pick the right film, or dismiss the row if it is not a film at all —
          Letterboxd lets you log television, which never matches a film search.
        </p>
      </div>

      <ul className="flex flex-col gap-4">
        {items.map((item) => (
          <ReviewRow key={item.itemId} item={item} jobId={jobId} />
        ))}
      </ul>
    </div>
  );
}

function ReviewRow({ item, jobId }: { item: ImportReviewItem; jobId: string }) {
  return (
    <li className="flex flex-col gap-4 rounded-lg border border-neutral-200 p-4 dark:border-neutral-800">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <div className="flex flex-col gap-0.5">
          <h2 className="font-medium">
            {item.name}
            {item.year ? (
              <span className="font-normal text-neutral-600 dark:text-neutral-400">
                {" "}
                ({item.year})
              </span>
            ) : null}
          </h2>

          <p className="text-xs text-neutral-600 dark:text-neutral-400">
            From your export
            {item.rating !== null ? ` · rated ${item.rating}` : ""}
            {item.watchedOn !== null ? ` · watched ${item.watchedOn}` : ""}
          </p>
        </div>

        {/*
          A form, not a link: dismissing changes state, and ADR-0004's CSRF
          defence depends on that being a POST Next can origin-check.
        */}
        <form action={dismissRow}>
          <input type="hidden" name="itemId" value={item.itemId} />
          <input type="hidden" name="jobId" value={jobId} />
          <button
            type="submit"
            className="rounded-md border border-neutral-300 px-3 py-1.5 text-sm hover:bg-neutral-50 dark:border-neutral-700 dark:hover:bg-neutral-800"
          >
            Not a film
          </button>
        </form>
      </div>

      {item.candidates.length === 0 ? (
        <p className="text-sm text-neutral-600 dark:text-neutral-400">
          No films matched this title. It is most likely a television series or
          episode.
        </p>
      ) : (
        <ul className="flex flex-wrap gap-3">
          {item.candidates.map((candidate) => (
            <li key={candidate.movieId}>
              <CandidateChoice candidate={candidate} itemId={item.itemId} jobId={jobId} />
            </li>
          ))}
        </ul>
      )}
    </li>
  );
}

function CandidateChoice({
  candidate,
  itemId,
  jobId,
}: {
  candidate: ImportCandidate;
  itemId: string;
  jobId: string;
}) {
  const year = candidate.releaseDate?.slice(0, 4);

  return (
    <form action={resolveRow}>
      <input type="hidden" name="itemId" value={itemId} />
      <input type="hidden" name="jobId" value={jobId} />
      <input type="hidden" name="movieId" value={candidate.movieId} />

      <button
        type="submit"
        className="flex w-36 flex-col gap-2 rounded-md border border-neutral-300 p-2 text-left hover:border-blue-600 hover:bg-neutral-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-700 dark:hover:bg-neutral-800"
      >
        <div className="relative aspect-[2/3] w-full overflow-hidden rounded bg-neutral-200 dark:bg-neutral-800">
          {candidate.posterPath ? (
            <Image
              src={`${POSTER_BASE_URL}${candidate.posterPath}`}
              alt=""
              aria-hidden="true"
              fill
              sizes="9rem"
              className="object-cover"
            />
          ) : null}
        </div>

        <span className="text-sm leading-snug font-medium">{candidate.title}</span>

        <span className="text-xs text-neutral-600 dark:text-neutral-400">
          {year ?? "Year unknown"}
          {/*
            TMDb's community rating is usually what separates the film someone
            means from the obscure short sharing its name.
          */}
          {candidate.averageRating !== null ? ` · ★ ${candidate.averageRating.toFixed(1)}` : ""}
        </span>
      </button>
    </form>
  );
}
