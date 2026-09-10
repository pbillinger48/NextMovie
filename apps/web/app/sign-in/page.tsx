import type { Metadata } from "next";
import { redirect } from "next/navigation";

import { signIn } from "@/app/actions/auth";
import { CredentialsForm } from "@/app/components/CredentialsForm";
import { GoogleSignInButton } from "@/app/components/GoogleSignInButton";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "Sign in · NextMovie" };

type SignInPageProps = {
  searchParams: Promise<{ google?: string | string[] }>;
};

export default async function SignInPage({ searchParams }: SignInPageProps) {
  // Already signed in: showing a sign-in form would invite a second session for
  // no reason, and every extra session is another refresh token family.
  if (isSignedIn(await getSession())) {
    redirect("/");
  }

  const { google } = await searchParams;
  const googleMessage = messageFor(Array.isArray(google) ? google[0] : google);

  return (
    <div className="flex w-full flex-col items-center gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">Sign in</h1>

      {googleMessage ? (
        <p
          role="alert"
          className="w-full max-w-sm rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
        >
          {googleMessage}
        </p>
      ) : null}

      <div className="flex w-full max-w-sm flex-col gap-4">
        <GoogleSignInButton label="Continue with Google" />

        <div className="flex items-center gap-3 text-xs uppercase tracking-wide text-neutral-500">
          <span className="h-px flex-1 bg-neutral-300 dark:bg-neutral-700" />
          or
          <span className="h-px flex-1 bg-neutral-300 dark:bg-neutral-700" />
        </div>
      </div>

      <CredentialsForm
        action={signIn}
        submitLabel="Sign in"
        alternative={{
          prompt: "No account yet?",
          href: "/sign-up",
          label: "Create one",
        }}
      />
    </div>
  );
}

/**
 * Turns the callback's reason code into something worth reading.
 *
 * The callback reports only these three, and deliberately not why a sign-in
 * failed — the detail helps nobody legitimate and helps somebody probing it.
 */
function messageFor(reason: string | undefined): string | null {
  switch (reason) {
    case "cancelled":
      return "Google sign-in was cancelled.";
    case "unverified":
      return "That Google account's email address is not verified. Verify it with Google and try again.";
    case "failed":
      return "Google sign-in did not complete. Please try again.";
    default:
      return null;
  }
}
