import type { WatchingOptions } from "@nextmovie/api-client";

export type { WatchingOptions };

/**
 * The claim a page is allowed to make about where a film can be watched.
 *
 * Separated from the component that renders it because these are rules rather
 * than markup, and they are the rules ADR-0010 is most insistent about: not
 * knowing is never reported as unavailable, and renting is never called
 * streaming. Kept here, they can be tested without a DOM.
 */
export type WatchSummary =
  | { kind: "unknown" }
  | { kind: "unavailable" }
  | { kind: "available"; lines: WatchLine[] };

/** One thing worth saying, and how confidently. */
export type WatchLine = {
  /** `now` means press play. `paid` costs money on top and is never streaming. */
  tone: "now" | "elsewhere" | "paid";
  text: string;
};

export function summariseWatch(watch: WatchingOptions): WatchSummary {
  // TMDb's provider data is not exhaustive, and nobody may have looked this film
  // up at all. "We have not asked" and "it is nowhere" are different claims.
  if (!watch.known) {
    return { kind: "unknown" };
  }

  const lines: WatchLine[] = [];

  if (watch.streamingOn.length > 0) {
    lines.push({ tone: "now", text: `Watch now on ${listServices(watch.streamingOn)}` });
  }

  if (watch.streamingElsewhere.length > 0) {
    lines.push({
      tone: "elsewhere",
      text: `Streaming on ${listServices(watch.streamingElsewhere)}`,
    });
  }

  if (watch.rentOrBuy.length > 0) {
    // Deliberately not the word "streaming". A rental is not "you can watch this
    // tonight" in the sense the product promises.
    lines.push({ tone: "paid", text: `Rent or buy from ${listServices(watch.rentOrBuy)}` });
  }

  return lines.length === 0 ? { kind: "unavailable" } : { kind: "available", lines };
}

/** "A", "A and B", "A, B and C" — these are read, not tabulated. */
export function listServices(names: string[]): string {
  if (names.length <= 1) {
    return names.join("");
  }

  return `${names.slice(0, -1).join(", ")} and ${names[names.length - 1]}`;
}
