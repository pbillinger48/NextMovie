"use server";

import { redirect } from "next/navigation";

import {
  dismissImportItem,
  resolveImportItem,
  startImport,
} from "@/lib/import-api";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

/**
 * Uploading an export, and deciding what its unresolved rows meant.
 *
 * Server Actions, so Next verifies the request's origin — the same-origin check
 * ADR-0004 asks for on anything that changes state.
 */

export type ImportFormState = { message?: string };

export async function uploadExport(
  _state: ImportFormState | undefined,
  formData: FormData,
): Promise<ImportFormState> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    return { message: "Your session has expired. Sign in again to continue." };
  }

  const file = formData.get("file");

  if (!(file instanceof File) || file.size === 0) {
    return { message: "Choose a .csv file exported from Letterboxd." };
  }

  const outcome = await startImport(session.accessToken, file);

  if (!outcome.ok) {
    return {
      message:
        outcome.error.kind === "rejected"
          ? outcome.error.message
          : "Could not start the import. Please try again shortly.",
    };
  }

  // Outside any try/catch: redirect signals by throwing.
  redirect(`/import/${outcome.data.id}`);
}

export async function resolveRow(formData: FormData): Promise<void> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const itemId = String(formData.get("itemId"));
  const movieId = String(formData.get("movieId"));
  const jobId = String(formData.get("jobId"));

  await resolveImportItem(session.accessToken, itemId, movieId);

  // Back to the same screen, one row shorter. A redirect rather than a refresh
  // so the browser's back button does not re-submit the choice.
  redirect(`/import/${jobId}/review`);
}

export async function dismissRow(formData: FormData): Promise<void> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const itemId = String(formData.get("itemId"));
  const jobId = String(formData.get("jobId"));

  await dismissImportItem(session.accessToken, itemId);

  redirect(`/import/${jobId}/review`);
}
