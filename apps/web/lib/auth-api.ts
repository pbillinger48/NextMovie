import type { AuthenticationResponse } from "@nextmovie/api-client";

import { api } from "./api-client";

/**
 * The API's authentication endpoints, as the web tier sees them.
 *
 * Per ADR-0004 the API only ever speaks bearer tokens; turning what it returns
 * into a browser session is this tier's job and happens in the actions and proxy
 * that call these functions. Nothing here touches a cookie.
 */

/** A failure worth showing the user, distinguished by what they can do about it. */
export type AuthFailure =
  | { kind: "invalid-credentials"; message: string }
  | { kind: "email-taken"; message: string }
  | { kind: "email-unverified"; message: string }
  | { kind: "validation"; message: string; fieldErrors: Record<string, string[]> }
  | { kind: "unreachable"; message: string };

export type AuthOutcome =
  | { ok: true; session: AuthenticationResponse }
  | { ok: false; error: AuthFailure };

/** Creates an account and signs it in. */
export async function register(
  email: string,
  displayName: string,
  password: string,
): Promise<AuthOutcome> {
  return call(() =>
    api.POST("/api/v1/auth/register", { body: { email, displayName, password } }),
  );
}

/** Exchanges credentials for a session. */
export async function login(email: string, password: string): Promise<AuthOutcome> {
  return call(() => api.POST("/api/v1/auth/login", { body: { email, password } }));
}

/**
 * Exchanges a refresh token for a new session.
 *
 * Every call rotates: the token passed in is dead afterwards, and the caller
 * must persist what comes back or the session is lost.
 */
export async function refresh(refreshToken: string): Promise<AuthOutcome> {
  return call(() => api.POST("/api/v1/auth/refresh", { body: { refreshToken } }));
}

/**
 * Exchanges a verified Google ID token for a NextMovie session.
 *
 * The API does the verifying (ADR-0005). This tier's job was getting the token
 * out of Google and putting the resulting session into a cookie.
 */
export async function signInWithGoogle(idToken: string): Promise<AuthOutcome> {
  return call(() => api.POST("/api/v1/auth/google", { body: { idToken } }));
}

/**
 * Ends the session the refresh token belongs to.
 *
 * Never throws and reports nothing. The API answers 204 whatever happens, and a
 * sign-out that failed loudly because the network blinked would leave the user
 * staring at an error while still holding a cookie we are about to delete
 * anyway.
 */
export async function logout(refreshToken: string): Promise<void> {
  try {
    await api.POST("/api/v1/auth/logout", { body: { refreshToken } });
  } catch {
    // Intentionally ignored: the cookie is cleared regardless.
  }
}

type ApiCall = () => Promise<{
  data?: AuthenticationResponse;
  error?: unknown;
  response: Response;
}>;

async function call(request: ApiCall): Promise<AuthOutcome> {
  let data: AuthenticationResponse | undefined;
  let error: unknown;
  let response: Response;

  try {
    ({ data, error, response } = await request());
  } catch {
    // The API is unreachable, which is a different problem from a rejected
    // credential and must not be reported to the user as one.
    return {
      ok: false,
      error: {
        kind: "unreachable",
        message: "Could not reach NextMovie. Please try again shortly.",
      },
    };
  }

  if (data) {
    return { ok: true, session: data };
  }

  switch (response.status) {
    case 401:
      // The API deliberately does not say whether the address exists, and
      // neither does this. Repeating its wording keeps that promise intact.
      return {
        ok: false,
        error: {
          kind: "invalid-credentials",
          message: "The email address or password is incorrect.",
        },
      };

    case 403:
      // Google authenticated them, but the address on the account is unverified
      // and ADR-0005 will not link or create on that basis.
      return {
        ok: false,
        error: {
          kind: "email-unverified",
          message:
            "That Google account's email address is not verified. Verify it with Google and try again.",
        },
      };

    case 409:
      return {
        ok: false,
        error: {
          kind: "email-taken",
          message: "An account already exists for that email address.",
        },
      };

    case 400:
      return {
        ok: false,
        error: {
          kind: "validation",
          message: "Please check the details below.",
          fieldErrors: fieldErrorsFrom(error),
        },
      };

    default:
      return {
        ok: false,
        error: {
          kind: "unreachable",
          message: "Something went wrong. Please try again shortly.",
        },
      };
  }
}

/** Pulls ProblemDetails validation errors out, keyed by field. */
function fieldErrorsFrom(error: unknown): Record<string, string[]> {
  if (error && typeof error === "object" && "errors" in error) {
    const { errors } = error as { errors?: Record<string, string[]> };

    return errors ?? {};
  }

  return {};
}
