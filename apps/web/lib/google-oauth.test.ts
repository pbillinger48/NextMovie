import { createHash } from "node:crypto";

import { describe, expect, it } from "vitest";

import {
  beginFlow,
  idTokenNonce,
  sealPendingFlow,
  secretsMatch,
  unsealPendingFlow,
} from "./google-oauth";

/**
 * The parts of Google sign-in that are ours to get right.
 *
 * Google's half is Google's problem; these are the checks that stop somebody
 * feeding their own authorization code to a signed-in browser, or swapping in an
 * ID token from another flow. Each has a failure mode that looks like nothing at
 * all until it is exploited.
 */

const config = {
  clientId: "test-client.apps.googleusercontent.com",
  clientSecret: "test-secret",
  redirectUri: "http://localhost:3000/api/auth/google/callback",
};

describe("beginFlow", () => {
  it("asks Google for an authorization code with the scopes we need", () => {
    const { authorizeUrl } = beginFlow(config);
    const url = new URL(authorizeUrl);

    expect(`${url.origin}${url.pathname}`).toBe("https://accounts.google.com/o/oauth2/v2/auth");
    expect(url.searchParams.get("response_type")).toBe("code");
    expect(url.searchParams.get("scope")).toBe("openid email profile");
    expect(url.searchParams.get("client_id")).toBe(config.clientId);
    expect(url.searchParams.get("redirect_uri")).toBe(config.redirectUri);
    expect(url.searchParams.get("prompt")).toBe("select_account");
  });

  it("sends the PKCE challenge, never the verifier", () => {
    const { authorizeUrl, pending } = beginFlow(config);
    const url = new URL(authorizeUrl);

    // The whole point of PKCE: only the hash leaves this server, so an
    // intercepted authorization code cannot be redeemed without the secret that
    // never travelled.
    const expected = createHash("sha256").update(pending.codeVerifier).digest("base64url");

    expect(url.searchParams.get("code_challenge")).toBe(expected);
    expect(url.searchParams.get("code_challenge_method")).toBe("S256");
    expect(authorizeUrl).not.toContain(pending.codeVerifier);
  });

  it("puts the same state and nonce in the URL as in the pending flow", () => {
    const { authorizeUrl, pending } = beginFlow(config);
    const url = new URL(authorizeUrl);

    // If these ever diverged, every callback would be rejected — or worse, the
    // check would be comparing something to itself and catching nothing.
    expect(url.searchParams.get("state")).toBe(pending.state);
    expect(url.searchParams.get("nonce")).toBe(pending.nonce);
  });

  it("generates fresh secrets for every flow", () => {
    const first = beginFlow(config).pending;
    const second = beginFlow(config).pending;

    // A reused state or verifier would let one flow's callback complete another.
    expect(first.state).not.toBe(second.state);
    expect(first.codeVerifier).not.toBe(second.codeVerifier);
    expect(first.nonce).not.toBe(second.nonce);
  });

  it("generates secrets with enough entropy to be unguessable", () => {
    const { pending } = beginFlow(config);

    // 32 random bytes, base64url-encoded. Short values here would be guessable,
    // and a guessable state defeats the CSRF check entirely.
    expect(pending.state.length).toBeGreaterThanOrEqual(43);
    expect(pending.codeVerifier.length).toBeGreaterThanOrEqual(43);
  });
});

describe("the pending flow cookie", () => {
  it("survives a seal and unseal round trip", async () => {
    const { pending } = beginFlow(config);

    expect(await unsealPendingFlow(await sealPendingFlow(pending))).toEqual(pending);
  });

  it("hides its contents", async () => {
    const { pending } = beginFlow(config);
    const sealed = await sealPendingFlow(pending);

    // The browser holds this. A verifier readable from it would make PKCE
    // decorative.
    expect(sealed).not.toContain(pending.codeVerifier);
    expect(sealed).not.toContain(pending.state);
  });

  it("refuses a tampered cookie", async () => {
    const sealed = await sealPendingFlow(beginFlow(config).pending);
    const tampered = `${sealed.slice(0, -6)}AAAAAA`;

    expect(await unsealPendingFlow(tampered)).toBeNull();
  });

  it("refuses a cookie that is not sealed at all", async () => {
    expect(await unsealPendingFlow("plainly-not-sealed")).toBeNull();
  });
});

describe("idTokenNonce", () => {
  const tokenWith = (claims: object) =>
    [
      Buffer.from(JSON.stringify({ alg: "RS256" })).toString("base64url"),
      Buffer.from(JSON.stringify(claims)).toString("base64url"),
      "signature-not-checked-here",
    ].join(".");

  it("reads the nonce out of a token", () => {
    expect(idTokenNonce(tokenWith({ nonce: "the-nonce", sub: "123" }))).toBe("the-nonce");
  });

  it("returns null when there is no nonce", () => {
    // A token without one cannot be tied to the flow that started, so the
    // callback must refuse it rather than proceed with undefined.
    expect(idTokenNonce(tokenWith({ sub: "123" }))).toBeNull();
  });

  it("returns null when the nonce is not a string", () => {
    expect(idTokenNonce(tokenWith({ nonce: 42 }))).toBeNull();
  });

  it.each(["", "not-a-jwt", "only.two", "a..c"])("returns null for %s", (malformed) => {
    expect(idTokenNonce(malformed)).toBeNull();
  });
});

describe("secretsMatch", () => {
  it("accepts identical values", () => {
    expect(secretsMatch("abc123", "abc123")).toBe(true);
  });

  it.each([
    ["a difference at the end", "abc123", "abc124"],
    ["a difference at the start", "abc123", "zbc123"],
    ["a different length", "abc123", "abc1234"],
    ["an empty candidate", "abc123", ""],
  ])("rejects %s", (_label, a, b) => {
    expect(secretsMatch(a, b)).toBe(false);
  });

  it("accepts two empty strings", () => {
    // Not a security decision — callers check for presence first — but the
    // function should not surprise anyone by disagreeing with ===.
    expect(secretsMatch("", "")).toBe(true);
  });
});
