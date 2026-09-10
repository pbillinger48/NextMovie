import type { Metadata } from "next";
import { redirect } from "next/navigation";

import { signUp } from "@/app/actions/auth";
import { CredentialsForm } from "@/app/components/CredentialsForm";
import { GoogleSignInButton } from "@/app/components/GoogleSignInButton";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "Create an account · NextMovie" };

export default async function SignUpPage() {
  if (isSignedIn(await getSession())) {
    redirect("/");
  }

  return (
    <div className="flex w-full flex-col items-center gap-6">
      <div className="flex flex-col items-center gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">Create an account</h1>
        <p className="text-sm text-neutral-600 dark:text-neutral-400">
          Passwords must be at least 12 characters.
        </p>
      </div>

      <div className="flex w-full max-w-sm flex-col gap-4">
        <GoogleSignInButton label="Sign up with Google" />

        <div className="flex items-center gap-3 text-xs uppercase tracking-wide text-neutral-500">
          <span className="h-px flex-1 bg-neutral-300 dark:bg-neutral-700" />
          or
          <span className="h-px flex-1 bg-neutral-300 dark:bg-neutral-700" />
        </div>
      </div>

      <CredentialsForm
        action={signUp}
        withDisplayName
        submitLabel="Create account"
        alternative={{
          prompt: "Already have an account?",
          href: "/sign-in",
          label: "Sign in",
        }}
      />
    </div>
  );
}
