import { summariseWatch, type WatchingOptions } from "@/lib/watch-options";

/**
 * Where a film can be watched.
 *
 * One component for the recommendation list, the watchlist and the film page, so
 * the distinctions it draws cannot drift between them. The distinctions
 * themselves live in `lib/watch-options` — they are rules, not markup, and they
 * are tested there.
 */
export function WatchOptions({ watch }: { watch: WatchingOptions }) {
  const summary = summariseWatch(watch);

  if (summary.kind === "unknown") {
    return (
      <p className="text-xs text-neutral-500 dark:text-neutral-400">
        We don&apos;t know where to watch this.
      </p>
    );
  }

  if (summary.kind === "unavailable") {
    return (
      <p className="text-xs text-neutral-500 dark:text-neutral-400">
        Not streaming in your region.
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-1 text-xs">
      {summary.lines.map((line) => (
        <p key={line.tone} className={TONES[line.tone]}>
          {line.text}
        </p>
      ))}
    </div>
  );
}

const TONES: Record<"now" | "elsewhere" | "paid", string> = {
  now: "font-medium text-green-700 dark:text-green-400",
  elsewhere: "text-neutral-700 dark:text-neutral-300",
  paid: "text-neutral-600 dark:text-neutral-400",
};
