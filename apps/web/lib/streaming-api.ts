import type { StreamingSettingsResponse } from "@nextmovie/api-client";

import { api } from "./api-client";

/**
 * Where the signed-in user watches, and what they pay for.
 *
 * Shaped like `user-api` deliberately: the access token is passed in rather than
 * read from the session here, so every call site says whose settings it is
 * touching.
 */

export type StreamingFailure =
  | { kind: "unauthorized"; message: string }
  | { kind: "validation"; message: string; fieldErrors: Record<string, string[]> }
  | { kind: "unreachable"; message: string };

export type StreamingOutcome =
  | { ok: true; settings: StreamingSettingsResponse }
  | { ok: false; error: StreamingFailure };

/** Reads the settings the access token belongs to. */
export async function fetchStreamingSettings(accessToken: string): Promise<StreamingOutcome> {
  return call(() =>
    api.GET("/api/v1/users/me/streaming", { headers: authorization(accessToken) }),
  );
}

/**
 * Replaces the region and the whole set of subscribed services.
 *
 * `PUT` semantics: a service missing from `providerIds` is cancelled, which is
 * the only way unticking one can mean anything.
 */
export async function updateStreamingSettings(
  accessToken: string,
  region: string,
  providerIds: number[],
): Promise<StreamingOutcome> {
  return call(() =>
    api.PUT("/api/v1/users/me/streaming", {
      headers: authorization(accessToken),
      body: { region, providerIds },
    }),
  );
}

function authorization(accessToken: string): Record<string, string> {
  return { Authorization: `Bearer ${accessToken}` };
}

type ApiCall = () => Promise<{
  data?: StreamingSettingsResponse;
  error?: unknown;
  response: Response;
}>;

async function call(request: ApiCall): Promise<StreamingOutcome> {
  let data: StreamingSettingsResponse | undefined;
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
    return { ok: true, settings: data };
  }

  switch (response.status) {
    case 401:
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
