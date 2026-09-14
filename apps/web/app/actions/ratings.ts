"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";

import { rateMovie, unrateMovie } from "@/lib/ratings-api";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

/**
 * Rating a film from its page.
 *
 * Server Actions, so Next origin-checks them — the same-origin defence ADR-0004
 * asks for on anything that changes state.
 */

export async function rate(formData: FormData): Promise<void> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const movieId = String(formData.get("movieId"));
  const rating = Number(formData.get("rating"));

  await rateMovie(session.accessToken, movieId, rating);

  // The page renders the current rating from the server, so it has to re-read.
  revalidatePath(`/movies/${movieId}`);
}

export async function unrate(formData: FormData): Promise<void> {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const movieId = String(formData.get("movieId"));

  await unrateMovie(session.accessToken, movieId);

  revalidatePath(`/movies/${movieId}`);
}
