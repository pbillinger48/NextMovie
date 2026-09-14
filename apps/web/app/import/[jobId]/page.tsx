import type { Metadata } from "next";
import Link from "next/link";
import { notFound, redirect } from "next/navigation";

import { AutoRefresh } from "@/app/components/AutoRefresh";
import { getImportStatus, type ImportJobStatus } from "@/lib/import-api";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "Import · NextMovie" };

/** How often to re-check a running import. */
const POLL_INTERVAL_MS = 3_000;

type ImportStatusPageProps = {
  params: Promise<{ jobId: string }>;
};

export default async function ImportStatusPage({ params }: ImportStatusPageProps) {
  const { jobId } = await params;
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const result = await getImportStatus(session.accessToken, jobId);

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

  const job = result.data;
  const inProgress = job.status === "Pending" || job.status === "Running";
  const needsReview = job.ambiguousItems + job.unresolvedItems;

  return (
    <div className="flex flex-col gap-8">
      {/*
        Only rendered while the job is in progress, which is also how the polling
        stops: once it finishes, the component is gone and the interval with it.
      */}
      {inProgress ? <AutoRefresh intervalMs={POLL_INTERVAL_MS} /> : null}

      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          {inProgress ? "Importing your films" : "Import finished"}
        </h1>

        <p aria-live="polite" className="text-sm text-neutral-600 dark:text-neutral-400">
          {describe(job)}
        </p>
      </div>

      <Progress job={job} />

      {job.status === "Failed" ? (
        <p
          role="alert"
          className="max-w-prose rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
        >
          {job.failureReason ?? "The import stopped unexpectedly."} Nothing was lost —
          upload the same file again to pick up where it left off.
        </p>
      ) : null}

      {!inProgress && needsReview > 0 ? (
        <div className="flex max-w-prose flex-col items-start gap-3 rounded-md border border-amber-300 bg-amber-50 px-4 py-3 dark:border-amber-900 dark:bg-amber-950">
          <p className="text-sm text-amber-900 dark:text-amber-100">
            {needsReview === 1
              ? "One film could not be identified with confidence."
              : `${needsReview} films could not be identified with confidence.`}{" "}
            They were left out rather than guessed at.
          </p>

          <Link
            href={`/import/${job.id}/review`}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            Review them
          </Link>
        </div>
      ) : null}

      {!inProgress && needsReview === 0 && job.status === "Completed" ? (
        <Link
          href="/profile"
          className="self-start rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
        >
          Done
        </Link>
      ) : null}
    </div>
  );
}

function describe(job: ImportJobStatus): string {
  switch (job.status) {
    case "Pending":
      return "Queued. This starts within a few seconds.";
    case "Running":
      return `Matching ${job.totalItems.toLocaleString()} films against TMDb…`;
    case "Failed":
      return "The import did not finish.";
    default:
      return `${job.matchedItems.toLocaleString()} of ${job.totalItems.toLocaleString()} films imported.`;
  }
}

function Progress({ job }: { job: ImportJobStatus }) {
  const settled = job.matchedItems + job.ambiguousItems + job.unresolvedItems;

  return (
    <div className="flex max-w-lg flex-col gap-3">
      <div
        role="progressbar"
        aria-valuenow={settled}
        aria-valuemin={0}
        aria-valuemax={job.totalItems}
        aria-label="Films processed"
        className="h-2 w-full overflow-hidden rounded-full bg-neutral-200 dark:bg-neutral-800"
      >
        <div
          className="h-full rounded-full bg-blue-600 transition-[width] duration-500"
          style={{ width: `${job.totalItems === 0 ? 0 : (settled / job.totalItems) * 100}%` }}
        />
      </div>

      <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
        <Count label="Imported" value={job.matchedItems} />
        <Count label="Need review" value={job.ambiguousItems} />
        <Count label="Not found" value={job.unresolvedItems} />
        {/*
          Rows the file contained that carried no title. Shown rather than
          quietly dropped: someone who exported 800 films and imported 797 should
          be able to see where the other three went.
        */}
        <Count label="Skipped rows" value={job.skippedRows} />
      </dl>
    </div>
  );
}

function Count({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex flex-col">
      <dt className="text-neutral-600 dark:text-neutral-400">{label}</dt>
      <dd className="text-lg font-medium tabular-nums">{value.toLocaleString()}</dd>
    </div>
  );
}
