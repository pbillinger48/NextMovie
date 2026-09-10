import Link from "next/link";

import { signOut } from "@/app/actions/auth";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

/**
 * Signed-in state in the header.
 *
 * A server component, so the session is read where the cookie can actually be
 * decrypted and no token ever reaches the browser — the display name it renders
 * is the only part of the session the page ever sees.
 */
export async function SessionNav() {
  const session = await getSession();

  if (!isSignedIn(session)) {
    return (
      <nav className="flex items-center gap-4 text-sm">
        <Link href="/sign-in" className="hover:underline underline-offset-4">
          Sign in
        </Link>
        <Link
          href="/sign-up"
          className="rounded-md bg-blue-600 px-3 py-1.5 font-medium text-white hover:bg-blue-700"
        >
          Create account
        </Link>
      </nav>
    );
  }

  return (
    <nav className="flex items-center gap-4 text-sm">
      <Link
        href="/profile"
        className="text-neutral-600 hover:underline underline-offset-4 dark:text-neutral-400"
      >
        {session.user.displayName}
      </Link>

      {/*
        A form, not a link: signing out changes state, and ADR-0004's CSRF
        defence depends on state-changing operations being POSTs that Next can
        origin-check.
      */}
      <form action={signOut}>
        <button type="submit" className="hover:underline underline-offset-4">
          Sign out
        </button>
      </form>
    </nav>
  );
}
