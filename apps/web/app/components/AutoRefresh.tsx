"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

/**
 * Re-renders the page on an interval while an import is running.
 *
 * `router.refresh()` re-runs the server component, so the progress comes from
 * the same read as the first render — there is no second, client-side copy of
 * how to fetch a job status, and nothing to keep in sync.
 *
 * Renders nothing. It unmounts when the server stops asking for it, which is how
 * the polling stops: the page only includes this component while the job is
 * still in progress.
 */
export function AutoRefresh({ intervalMs }: { intervalMs: number }) {
  const router = useRouter();

  useEffect(() => {
    const timer = setInterval(() => router.refresh(), intervalMs);

    return () => clearInterval(timer);
  }, [router, intervalMs]);

  return null;
}
