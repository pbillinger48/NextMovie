"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";

import { respondToMovie, withdrawResponse, type MovieResponse } from "@/lib/responses-api";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

/**
 * Saving, dismissing and marking films seen.
 *
 * Server Actions, so Next origin-checks them — the same-origin defence ADR-0004
 * asks for on anything that changes state. Plain forms, like the rating control,
 * so the buttons work before any client JavaScript has loaded.
 */

const RESPONSES: readonly MovieResponse[] = ["Saved", "NotInterested", "Seen"];

export async function respond(formData: FormData): Promise<void> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const movieId = String(formData.get("movieId"));
  const response = RESPONSES.find((allowed) => allowed === formData.get("response"));

  // A form field is untrusted input even when this app wrote the form. Sending
  // an unrecognised value would earn a 400 the user never asked for.
  if (response === undefined) {
    return;
  }

  await respondToMovie(session.accessToken, movieId, response);

  revalidateAffected(movieId);
}

export async function withdraw(formData: FormData): Promise<void> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const movieId = String(formData.get("movieId"));

  await withdrawResponse(session.accessToken, movieId);

  revalidateAffected(movieId);
}

/**
 * Every page whose contents an answer changes.
 *
 * All three, always, rather than only the page the button was on: answering
 * about a film adds or removes it from the watchlist *and* drops it out of
 * recommendations *and* changes which buttons the film page shows. Revalidating
 * only the current page would leave the other two confidently stale.
 */
function revalidateAffected(movieId: string): void {
  revalidatePath(`/movies/${movieId}`);
  revalidatePath("/recommendations");
  revalidatePath("/watchlist");
}
