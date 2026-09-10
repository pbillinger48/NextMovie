import type { Metadata } from "next";
import { redirect } from "next/navigation";

import { signOut } from "@/app/actions/auth";
import { ProfileForm } from "@/app/components/ProfileForm";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";
import { fetchProfile } from "@/lib/user-api";

export const metadata: Metadata = { title: "Your profile · NextMovie" };

/**
 * The signed-in user's profile.
 *
 * The first page to make an authenticated API call while rendering, so it is the
 * first proof of the whole chain in one request: an encrypted cookie the browser
 * cannot read, unsealed on the server, turned into a bearer token, spent against
 * an endpoint that requires one.
 */
export default async function ProfilePage() {
  const session = await getSession();

  if (!isSignedIn(session)) {
    // No cookie at all. A redirect is safe here precisely because there is no
    // session for /sign-in to bounce back off.
    redirect("/sign-in");
  }

  const outcome = await fetchProfile(session.accessToken);

  if (!outcome.ok) {
    return <ProfileUnavailable message={outcome.error.message} />;
  }

  const { profile } = outcome;

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold tracking-tight">Your profile</h1>
        <p className="text-sm text-neutral-600 dark:text-neutral-400">
          Signed in as {profile.email}.
        </p>
      </div>

      <ProfileForm
        displayName={profile.displayName}
        profileImageUrl={profile.profileImageUrl ?? null}
      />
    </div>
  );
}

/**
 * Shown when the API will not serve the profile.
 *
 * Deliberately not a redirect to /sign-in. The cookie still says signed in, so
 * that page would bounce straight back here and the two would loop forever. A
 * server component cannot clear the cookie either — only actions, route handlers
 * and the proxy may write one — so the way out is the sign-out button, which is
 * an action and can.
 */
function ProfileUnavailable({ message }: { message: string }) {
  return (
    <div className="flex flex-col items-start gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">Your profile</h1>

      <p
        role="alert"
        className="rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
      >
        {message}
      </p>

      <form action={signOut}>
        <button
          type="submit"
          className="rounded-md bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600"
        >
          Sign out
        </button>
      </form>
    </div>
  );
}
