import type { Metadata } from "next";
import { redirect } from "next/navigation";

import { signUp } from "@/app/actions/auth";
import { CredentialsForm } from "@/app/components/CredentialsForm";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "Create an account · NextMovie" };

export default async function SignUpPage() {
  if (isSignedIn(await getSession())) {
    redirect("/");
  }

  return (
    <div className="flex flex-col items-center gap-6">
      <div className="flex flex-col items-center gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">Create an account</h1>
        <p className="text-sm text-neutral-600 dark:text-neutral-400">
          Passwords must be at least 12 characters.
        </p>
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
