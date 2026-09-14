import type {
  ImportCandidate as Candidate,
  ImportJobStatusResponse,
  ImportReviewItem as ReviewItem,
  ImportReviewResponse,
} from "@nextmovie/api-client";

import { api, apiBaseUrl } from "./api-client";

/**
 * The Letterboxd import, as the web tier sees it.
 *
 * Every call takes the access token explicitly, like the rest of the authenticated
 * API surface, so each call site shows whose behalf it acts on.
 */

export type ImportJobStatus = ImportJobStatusResponse;
export type ImportReview = ImportReviewResponse;
export type ImportReviewItem = ReviewItem;
export type ImportCandidate = Candidate;

export type ImportFailure =
  | { kind: "rejected"; message: string }
  | { kind: "not-found" }
  | { kind: "unreachable"; message: string };

export type ImportResult<T> = { ok: true; data: T } | { ok: false; error: ImportFailure };

const UNREACHABLE: ImportFailure = {
  kind: "unreachable",
  message: "Could not reach NextMovie. Please try again shortly.",
};

/**
 * Uploads an export and queues it.
 *
 * Sent with `fetch` rather than the generated client, and deliberately: the
 * OpenAPI document models the uploaded file as a binary *string* (ASP.NET
 * publishes it as `IFormFile`), so `openapi-fetch` would need a cast that lies
 * about the type either way. The response is still typed from the generated
 * schema, which is the part ADR-0002 actually protects.
 */
export async function startImport(
  accessToken: string,
  file: File,
): Promise<ImportResult<ImportJobStatus>> {
  const body = new FormData();
  body.append("file", file);

  let response: Response;

  try {
    response = await fetch(`${apiBaseUrl}/api/v1/import/letterboxd`, {
      method: "POST",
      headers: { Authorization: `Bearer ${accessToken}` },
      body,
    });
  } catch {
    return { ok: false, error: UNREACHABLE };
  }

  if (response.ok) {
    return { ok: true, data: (await response.json()) as ImportJobStatus };
  }

  if (response.status === 400) {
    // The API already explains what is wrong with the file — wrong extension,
    // too large, no films in it. Repeating its wording keeps one source of truth
    // for these messages.
    const problem = (await response.json().catch(() => null)) as
      | { errors?: Record<string, string[]> }
      | null;

    const detail = Object.values(problem?.errors ?? {})
      .flat()
      .join(" ");

    return {
      ok: false,
      error: { kind: "rejected", message: detail || "That file could not be read." },
    };
  }

  return { ok: false, error: UNREACHABLE };
}

export async function getImportStatus(
  accessToken: string,
  jobId: string,
): Promise<ImportResult<ImportJobStatus>> {
  return call(() =>
    api.GET("/api/v1/import/{jobId}", {
      params: { path: { jobId } },
      headers: authorization(accessToken),

      // Always fresh: this is a progress reading, and a cached one is worse than
      // no reading at all.
      cache: "no-store",
    }),
  );
}

export async function getImportReview(
  accessToken: string,
  jobId: string,
): Promise<ImportResult<ImportReview>> {
  return call(() =>
    api.GET("/api/v1/import/{jobId}/review", {
      params: { path: { jobId } },
      headers: authorization(accessToken),
      cache: "no-store",
    }),
  );
}

/** Applies a person's choice of which film an import row meant. */
export async function resolveImportItem(
  accessToken: string,
  itemId: string,
  movieId: string,
): Promise<ImportResult<ImportJobStatus>> {
  return call(() =>
    api.POST("/api/v1/import/items/{itemId}/resolve", {
      params: { path: { itemId } },
      headers: authorization(accessToken),
      body: { movieId },
    }),
  );
}

/** Records that a row is deliberately not being imported. */
export async function dismissImportItem(
  accessToken: string,
  itemId: string,
): Promise<ImportResult<ImportJobStatus>> {
  return call(() =>
    api.POST("/api/v1/import/items/{itemId}/dismiss", {
      params: { path: { itemId } },
      headers: authorization(accessToken),
    }),
  );
}

function authorization(accessToken: string): Record<string, string> {
  return { Authorization: `Bearer ${accessToken}` };
}

async function call<T>(
  request: () => Promise<{ data?: T; response: Response }>,
): Promise<ImportResult<T>> {
  let data: T | undefined;
  let response: Response;

  try {
    ({ data, response } = await request());
  } catch {
    return { ok: false, error: UNREACHABLE };
  }

  if (data) {
    return { ok: true, data };
  }

  // 404 covers both "no such import" and "not yours" — the API deliberately does
  // not distinguish them, and neither does this.
  return response.status === 404
    ? { ok: false, error: { kind: "not-found" } }
    : { ok: false, error: UNREACHABLE };
}
