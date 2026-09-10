import { describe, expect, it } from "vitest";

import { formatRuntime } from "./format";

describe("formatRuntime", () => {
  it("renders hours and minutes", () => {
    expect(formatRuntime(148)).toBe("2h 28m");
  });

  it("omits the hours for a short film", () => {
    expect(formatRuntime(42)).toBe("42m");
  });

  it("omits a zero minute remainder", () => {
    // "2h 0m" reads oddly and the zero says nothing.
    expect(formatRuntime(120)).toBe("2h");
  });

  it("handles exactly one hour", () => {
    expect(formatRuntime(60)).toBe("1h");
  });

  it("handles the minute either side of an hour", () => {
    expect(formatRuntime(59)).toBe("59m");
    expect(formatRuntime(61)).toBe("1h 1m");
  });

  it("handles a very long film", () => {
    expect(formatRuntime(321)).toBe("5h 21m");
  });
});
