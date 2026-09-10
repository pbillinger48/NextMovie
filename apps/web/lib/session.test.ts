import { describe, expect, it } from "vitest";

import {
  REFRESH_THRESHOLD_MS,
  applyAuthentication,
  isSignedIn,
  needsRefresh,
  type SessionData,
} from "./session";

/**
 * The session cookie's contents and the decision to refresh.
 *
 * `needsRefresh` is the one that matters most: too eager and every request
 * rotates a refresh token, which is a write and a chance to trip replay
 * detection; too lazy and requests are served with a token that expires
 * mid-flight.
 */

const NOW = Date.parse("2026-09-10T12:00:00.000Z");
const at = (offsetMs: number) => new Date(NOW + offsetMs).toISOString();

describe("needsRefresh", () => {
  it("leaves a comfortably live token alone", () => {
    expect(needsRefresh(at(10 * 60_000), NOW)).toBe(false);
  });

  it("refreshes once the token is inside the threshold", () => {
    expect(needsRefresh(at(REFRESH_THRESHOLD_MS - 1_000), NOW)).toBe(true);
  });

  it("treats the threshold itself as due", () => {
    // The boundary is deliberate rather than incidental: a token expiring in
    // exactly the threshold should be replaced, not squeaked through.
    expect(needsRefresh(at(REFRESH_THRESHOLD_MS), NOW)).toBe(true);
  });

  it("does not refresh one millisecond outside the threshold", () => {
    expect(needsRefresh(at(REFRESH_THRESHOLD_MS + 1), NOW)).toBe(false);
  });

  it("refreshes a token that has already expired", () => {
    expect(needsRefresh(at(-60_000), NOW)).toBe(true);
  });

  it("refreshes when the expiry cannot be parsed", () => {
    // A malformed or older cookie format. Refreshing risks one needless
    // rotation; trusting it risks sending a long-dead token.
    expect(needsRefresh("not a date", NOW)).toBe(true);
  });
});

describe("isSignedIn", () => {
  const complete: SessionData = {
    accessToken: "access",
    accessTokenExpiresAt: at(60_000),
    refreshToken: "refresh",
    user: { id: "u1", email: "parker@example.com", displayName: "Parker" },
  };

  it("accepts a complete session", () => {
    expect(isSignedIn(complete)).toBe(true);
  });

  it("rejects an empty session", () => {
    // What an anonymous visitor's cookie unseals to.
    expect(isSignedIn({})).toBe(false);
  });

  it.each(["accessToken", "accessTokenExpiresAt", "refreshToken", "user"] as const)(
    "rejects a session missing %s",
    (field) => {
      // A partial session is not a lesser session — it is one the rest of the
      // app would dereference and crash on.
      const partial: SessionData = { ...complete, [field]: undefined };

      expect(isSignedIn(partial)).toBe(false);
    },
  );
});

describe("applyAuthentication", () => {
  it("copies every field the session needs", () => {
    const session: SessionData = {};

    applyAuthentication(session, {
      accessToken: "new-access",
      accessTokenExpiresAt: at(15 * 60_000),
      refreshToken: "new-refresh",
      refreshTokenExpiresAt: at(30 * 24 * 60 * 60_000),
      user: {
        id: "u1",
        email: "parker@example.com",
        displayName: "Parker",
        profileImageUrl: null,
      },
    });

    // If a field is ever added to the session and not copied here, sign-in and
    // refresh diverge — one path stores it and the other quietly does not.
    expect(isSignedIn(session)).toBe(true);
    expect(session.accessToken).toBe("new-access");
    expect(session.refreshToken).toBe("new-refresh");
    expect(session.user).toEqual({
      id: "u1",
      email: "parker@example.com",
      displayName: "Parker",
    });
  });

  it("replaces a previous session rather than merging with it", () => {
    const session: SessionData = {
      accessToken: "old-access",
      accessTokenExpiresAt: at(-60_000),
      refreshToken: "old-refresh",
      user: { id: "u1", email: "old@example.com", displayName: "Old" },
    };

    applyAuthentication(session, {
      accessToken: "new-access",
      accessTokenExpiresAt: at(15 * 60_000),
      refreshToken: "new-refresh",
      refreshTokenExpiresAt: at(30 * 24 * 60 * 60_000),
      user: { id: "u2", email: "new@example.com", displayName: "New", profileImageUrl: null },
    });

    // A rotated refresh token that did not overwrite its predecessor would be
    // presented again on the next refresh and read as a replay.
    expect(session.refreshToken).toBe("new-refresh");
    expect(session.user?.email).toBe("new@example.com");
  });
});
