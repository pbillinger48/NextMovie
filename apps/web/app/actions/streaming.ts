"use server";

import { revalidatePath } from "next/cache";

import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";
import * as streamingApi from "@/lib/streaming-api";

export type StreamingFormState = {
  message?: string;
  /** Set on success, so the form can confirm rather than just going quiet. */
  saved?: boolean;
  fieldErrors?: Record<string, string[]>;
};

/**
 * Saves where the user watches and what they subscribe to.
 *
 * Region and services are sent together because the API replaces both at once.
 * Posting them separately would leave a moment where the region says one country
 * and the services belong to another, and anything recommended in between would
 * be wrong in a way nobody could see.
 */
export async function saveStreamingSettings(
  _state: StreamingFormState | undefined,
  formData: FormData,
): Promise<StreamingFormState> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    return { message: "Your session has expired. Sign in again to continue." };
  }

  const region = typeof formData.get("region") === "string" ? String(formData.get("region")) : "";

  // getAll, not get: unticked boxes send nothing at all, so the absent ones are
  // exactly the cancellations the API is being told about.
  const providerIds = formData
    .getAll("providerIds")
    .map((value) => Number(value))
    .filter((id) => Number.isInteger(id));

  const outcome = await streamingApi.updateStreamingSettings(
    session.accessToken,
    region,
    providerIds,
  );

  if (!outcome.ok) {
    return {
      message: outcome.error.message,
      fieldErrors: outcome.error.kind === "validation" ? outcome.error.fieldErrors : undefined,
    };
  }

  // Availability decides what recommendations say and how they rank, so the
  // recommendations page is stale the moment this is saved.
  revalidatePath("/profile/streaming");
  revalidatePath("/recommendations");

  return { saved: true };
}
