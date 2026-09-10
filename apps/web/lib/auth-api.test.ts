import { afterEach, describe, expect, it, vi } from "vitest";

/**
 * How the API's failures are translated for the user.
 *
 * This mapping decides what somebody is told when sign-in does not work, and one
 * of its jobs is to say *less* than it knows: the API refuses to reveal whether
 * an address has an account, and repeating its wording is what keeps that promise
 * from being undone one tier up.
 */

const session = {
  accessToken: "access",
  accessTokenExpiresAt: "2026-09-10T12:15:00Z",
  refreshToken: "refresh",
  refreshTokenExpiresAt: "2026-10-10T12:00:00Z",
  user: {
    id: "u1",
    email: "parker@example.com",
    displayName: "Parker",
    profileImageUrl: null,
  },
};

/**
 * Loads the module under test against a stubbed `fetch`.
 *
 * The import has to happen *after* the stub is installed: openapi-fetch captures
 * `globalThis.fetch` when the client is created, which is at module load. A stub
 * applied later is never consulted — the first version of these tests quietly hit
 * a real socket, and every case came back "unreachable" including the ones that
 * were supposed to be 401s.
 */
async function authApiReturning(status: number, body: unknown) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () =>
      new Response(JSON.stringify(body), {
        status,
        headers: { "content-type": status >= 400 ? "application/problem+json" : "application/json" },
      }),
    ),
  );

  vi.resetModules();

  return import("./auth-api");
}

/** The same, for an API that cannot be reached at all. */
async function authApiThatCannotConnect() {
  vi.stubGlobal("fetch", vi.fn(async () => { throw new TypeError("fetch failed"); }));
  vi.resetModules();

  return import("./auth-api");
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("a successful call", () => {
  it("returns the session the API issued", async () => {
    const { login } = await authApiReturning(200, session);

    const outcome = await login("parker@example.com", "correct horse battery staple");

    expect(outcome.ok).toBe(true);
    expect(outcome.ok && outcome.session.accessToken).toBe("access");
  });
});

describe("failure mapping", () => {
  it("reports 401 without revealing whether the account exists", async () => {
    const { login } = await authApiReturning(401, { title: "Invalid credentials", status: 401 });

    const outcome = await login("parker@example.com", "wrong");

    expect(outcome.ok).toBe(false);

    if (!outcome.ok) {
      expect(outcome.error.kind).toBe("invalid-credentials");

      // Says "or", not which. A message naming one or the other would turn the
      // sign-in form into the account oracle the API refuses to be.
      expect(outcome.error.message).toContain("email address or password");
    }
  });

  it("reports 409 as a taken email", async () => {
    const { register } = await authApiReturning(409, {
      title: "Email already registered",
      status: 409,
    });

    const outcome = await register("parker@example.com", "Parker", "correct horse battery staple");

    expect(outcome.ok).toBe(false);
    expect(!outcome.ok && outcome.error.kind).toBe("email-taken");
  });

  it("distinguishes an unverified Google address", async () => {
    const { signInWithGoogle } = await authApiReturning(403, {
      title: "Google email not verified",
      status: 403,
    });

    const outcome = await signInWithGoogle("an-id-token");

    expect(outcome.ok).toBe(false);

    // The callback branches on this to tell the user something they can act on,
    // rather than the generic "did not complete".
    expect(!outcome.ok && outcome.error.kind).toBe("email-unverified");
  });

  it("carries field errors out of a 400", async () => {
    const { register } = await authApiReturning(400, {
      title: "One or more validation errors occurred.",
      status: 400,
      errors: { Password: ["A password must be at least 12 characters."] },
    });

    const outcome = await register("parker@example.com", "Parker", "short");

    expect(outcome.ok).toBe(false);

    if (!outcome.ok && outcome.error.kind === "validation") {
      // Without this the form shows a general message and no indication of which
      // field is wrong.
      expect(outcome.error.fieldErrors.Password).toEqual([
        "A password must be at least 12 characters.",
      ]);
    } else {
      expect.unreachable("expected a validation failure");
    }
  });

  it("survives a 400 with no errors object", async () => {
    const { register } = await authApiReturning(400, { title: "Bad Request", status: 400 });

    const outcome = await register("parker@example.com", "Parker", "short");

    expect(!outcome.ok && outcome.error.kind).toBe("validation");
  });

  it("treats an unexpected status as a transient problem", async () => {
    const { login } = await authApiReturning(500, { title: "Server error", status: 500 });

    const outcome = await login("parker@example.com", "correct horse battery staple");

    expect(!outcome.ok && outcome.error.kind).toBe("unreachable");
  });

  it("distinguishes an unreachable API from a rejected credential", async () => {
    const { login } = await authApiThatCannotConnect();

    const outcome = await login("parker@example.com", "correct horse battery staple");

    expect(outcome.ok).toBe(false);

    // Telling someone their password is wrong when the API is simply down sends
    // them to reset a password that was never the problem.
    expect(!outcome.ok && outcome.error.kind).toBe("unreachable");
  });
});

describe("logout", () => {
  it("does not throw when the API is unreachable", async () => {
    const { logout } = await authApiThatCannotConnect();

    // Sign-out clears the cookie regardless. An exception here would leave the
    // user staring at an error while still signed in.
    await expect(logout("a-refresh-token")).resolves.toBeUndefined();
  });
});
