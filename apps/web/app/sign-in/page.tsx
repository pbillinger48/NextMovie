import type { Metadata } from "next";
import { redirect } from "next/navigation";

import { signIn } from "@/app/actions/auth";
import { CredentialsForm } from "@/app/components/CredentialsForm";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "Sign in · NextMovie" };

export default async function SignInPage() {
  // Already signed in: showing a sign-in form would invite a second session for
  // no reason, and every extra session is another refresh token family.
  if (isSignedIn(await getSession())) {
    redirect("/");
  }

  return (
    <div className="flex flex-col items-center gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">Sign in</h1>

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
