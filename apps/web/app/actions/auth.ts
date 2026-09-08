"use server";

import { redirect } from "next/navigation";

import * as authApi from "@/lib/auth-api";
import { applyAuthentication } from "@/lib/session";
import { getSession } from "@/lib/server-session";

/**
 * Sign-in, sign-up and sign-out.
 *
 * Server Actions rather than route handlers, and not only for convenience:
 * Next verifies the request's Origin against its Host for every action
 * invocation. That is the same-origin check ADR-0004 requires on mutating
 * handlers, which a route handler would need written by hand.
 *
 * These are also the only places allowed to write the session cookie — server
 * components may read it but the App Router will not let them set one.
 */

export type AuthFormState = {
  /** Shown above the form. */
  message?: string;
  fieldErrors?: Record<string, string[]>;
  /** Echoed back so a rejected form does not make the user retype everything. */
  values?: { email?: string; displayName?: string };
};

export async function signIn(
  _state: AuthFormState | undefined,
  formData: FormData,
): Promise<AuthFormState> {
  const email = stringField(formData, "email");
  const password = stringField(formData, "password");

  const outcome = await authApi.login(email, password);

  if (!outcome.ok) {
    // Note what is not echoed back: the password. Everything else returns so
    // the form can be refilled.
    return failure(outcome.error, { email });
  }

  const session = await getSession();
  applyAuthentication(session, outcome.session);
  await session.save();

  // Outside any try/catch on purpose: redirect signals by throwing, and
  // catching it here would swallow the navigation and look like a hang.
  redirect("/");
}

export async function signUp(
  _state: AuthFormState | undefined,
  formData: FormData,
): Promise<AuthFormState> {
  const email = stringField(formData, "email");
  const displayName = stringField(formData, "displayName");
  const password = stringField(formData, "password");

  const outcome = await authApi.register(email, displayName, password);

  if (!outcome.ok) {
    return failure(outcome.error, { email, displayName });
  }

  const session = await getSession();
  applyAuthentication(session, outcome.session);
  await session.save();

  redirect("/");
}

export async function signOut(): Promise<void> {
  const session = await getSession();
  const { refreshToken } = session;

  // Destroy the cookie first. If telling the API took an error path, the user
  // would otherwise still be signed in locally after clicking sign out — the
  // one outcome this must never produce.
  session.destroy();

  if (refreshToken !== undefined) {
    // Ends the session server-side too, so the refresh token cannot be reused
    // if the cookie was captured before now.
    await authApi.logout(refreshToken);
  }

  redirect("/");
}

function failure(
  error: authApi.AuthFailure,
  values: AuthFormState["values"],
): AuthFormState {
  return {
    message: error.message,
    fieldErrors: error.kind === "validation" ? error.fieldErrors : undefined,
    values,
  };
}

function stringField(formData: FormData, name: string): string {
  const value = formData.get(name);

  return typeof value === "string" ? value : "";
}
