"use server";

import { revalidatePath } from "next/cache";

import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";
import * as userApi from "@/lib/user-api";

export type ProfileFormState = {
  message?: string;
  /** Set on success, so the form can confirm rather than just going quiet. */
  saved?: boolean;
  fieldErrors?: Record<string, string[]>;
};

/**
 * Saves the profile on the signed-in user's behalf.
 *
 * The user id is never taken from the form — it comes from the session cookie,
 * which comes from the access token. A hidden field carrying an id would let
 * anyone edit anyone.
 */
export async function saveProfile(
  _state: ProfileFormState | undefined,
  formData: FormData,
): Promise<ProfileFormState> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    return { message: "Your session has expired. Sign in again to continue." };
  }

  const displayName = stringField(formData, "displayName");
  const profileImageUrl = stringField(formData, "profileImageUrl");

  const outcome = await userApi.updateProfile(
    session.accessToken,
    displayName,
    // An empty field means "no image", which PUT stores as cleared.
    profileImageUrl === "" ? undefined : profileImageUrl,
  );

  if (!outcome.ok) {
    return {
      message: outcome.error.message,
      fieldErrors: outcome.error.kind === "validation" ? outcome.error.fieldErrors : undefined,
    };
  }

  // The display name is rendered in the header from the session cookie, and the
  // profile page reads it from the API. Keep the cookie in step so the header
  // does not keep showing the old name until the next refresh.
  session.user = { ...session.user, displayName: outcome.profile.displayName };
  await session.save();

  // The header is rendered in the layout, so the whole tree needs re-rendering
  // for the new name to appear.
  revalidatePath("/", "layout");

  return { saved: true };
}

function stringField(formData: FormData, name: string): string {
  const value = formData.get(name);

  return typeof value === "string" ? value.trim() : "";
}
