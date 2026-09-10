import type { UserProfileResponse } from "@nextmovie/api-client";

import { api } from "./api-client";

/**
 * The signed-in user's profile, as the web tier sees it.
 *
 * Every call takes the access token explicitly rather than reaching for the
 * session itself. That keeps this module usable from anywhere on the server —
 * and makes it obvious at every call site that a request is being made on a
 * specific user's behalf.
 */

export type ProfileFailure =
  | { kind: "unauthorized"; message: string }
  | { kind: "validation"; message: string; fieldErrors: Record<string, string[]> }
  | { kind: "unreachable"; message: string };

export type ProfileOutcome =
  | { ok: true; profile: UserProfileResponse }
  | { ok: false; error: ProfileFailure };

/** Reads the profile the access token belongs to. */
export async function fetchProfile(accessToken: string): Promise<ProfileOutcome> {
  return call(() =>
    api.GET("/api/v1/users/me", { headers: authorization(accessToken) }),
  );
}

/**
 * Replaces the editable parts of the profile.
 *
 * `PUT` semantics: passing an undefined image clears it. The form always sends
 * both fields, so what the user sees is what gets stored.
 */
export async function updateProfile(
  accessToken: string,
  displayName: string,
  profileImageUrl: string | undefined,
): Promise<ProfileOutcome> {
  return call(() =>
    api.PUT("/api/v1/users/me", {
      headers: authorization(accessToken),
      body: { displayName, profileImageUrl: profileImageUrl ?? null },
    }),
  );
}

function authorization(accessToken: string): Record<string, string> {
  return { Authorization: `Bearer ${accessToken}` };
}

type ApiCall = () => Promise<{
  data?: UserProfileResponse;
  error?: unknown;
  response: Response;
}>;

async function call(request: ApiCall): Promise<ProfileOutcome> {
  let data: UserProfileResponse | undefined;
  let error: unknown;
  let response: Response;

  try {
    ({ data, error, response } = await request());
  } catch {
    return {
      ok: false,
      error: {
        kind: "unreachable",
        message: "Could not reach NextMovie. Please try again shortly.",
      },
    };
  }

  if (data) {
    return { ok: true, profile: data };
  }

  switch (response.status) {
    case 401:
      // The token was rejected: expired between the proxy's refresh and this
      // call, or the account is gone. Callers decide what to do about it —
      // a server component cannot clear the cookie itself.
      return {
        ok: false,
        error: {
          kind: "unauthorized",
          message: "Your session has expired. Sign in again to continue.",
        },
      };

    case 400:
      return {
        ok: false,
        error: {
          kind: "validation",
          message: "Please check the details below.",
          fieldErrors:
            error && typeof error === "object" && "errors" in error
              ? ((error as { errors?: Record<string, string[]> }).errors ?? {})
              : {},
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
