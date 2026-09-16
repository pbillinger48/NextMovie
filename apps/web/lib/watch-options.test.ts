import { describe, expect, it } from "vitest";

import { listServices, summariseWatch, type WatchingOptions } from "./watch-options";

/**
 * The two rules ADR-0010 is most insistent about, and the ones a redesign is
 * most likely to quietly break: not knowing is not the same as unavailable, and
 * renting is never described as streaming.
 */
function watch(overrides: Partial<WatchingOptions> = {}): WatchingOptions {
  return {
    streamingOn: [],
    streamingElsewhere: [],
    rentOrBuy: [],
    canStreamNow: false,
    known: true,
    link: null,
    ...overrides,
  };
}

describe("summariseWatch", () => {
  it("says it does not know rather than claiming nothing is available", () => {
    // TMDb's provider data is patchy, and a film nobody has looked up has been
    // asked about by nobody. Reporting either as "unavailable" is a claim we
    // cannot support.
    expect(summariseWatch(watch({ known: false }))).toEqual({ kind: "unknown" });
  });

  it("distinguishes asked-and-nowhere from never-asked", () => {
    expect(summariseWatch(watch()).kind).toBe("unavailable");
  });

  it("leads with a service you pay for", () => {
    const summary = summariseWatch(
      watch({ streamingOn: ["Netflix"], rentOrBuy: ["Apple TV"], canStreamNow: true }),
    );

    expect(summary.kind).toBe("available");
    expect(summary.kind === "available" && summary.lines[0]).toEqual({
      tone: "now",
      text: "Watch now on Netflix",
    });
  });

  it("never calls renting streaming", () => {
    const summary = summariseWatch(watch({ rentOrBuy: ["Apple TV", "Amazon Video"] }));

    expect(summary.kind).toBe("available");

    const lines = summary.kind === "available" ? summary.lines : [];
    expect(lines).toHaveLength(1);
    expect(lines[0].tone).toBe("paid");

    // A £3.49 rental is not "you can watch this tonight", and the product's own
    // sentence does not stretch to it.
    expect(lines[0].text).not.toMatch(/stream/i);
    expect(lines[0].text).toBe("Rent or buy from Apple TV and Amazon Video");
  });

  it("separates services you have from services you do not", () => {
    const summary = summariseWatch(
      watch({ streamingOn: ["Max"], streamingElsewhere: ["Hulu"], canStreamNow: true }),
    );

    const lines = summary.kind === "available" ? summary.lines : [];
    expect(lines.map((line) => line.tone)).toEqual(["now", "elsewhere"]);
    expect(lines[1].text).toBe("Streaming on Hulu");
  });
});

describe("listServices", () => {
  it.each([
    [[], ""],
    [["Netflix"], "Netflix"],
    [["Netflix", "Max"], "Netflix and Max"],
    [["Netflix", "Max", "Hulu"], "Netflix, Max and Hulu"],
  ])("reads %j as %j", (names, expected) => {
    expect(listServices(names)).toBe(expected);
  });
});
