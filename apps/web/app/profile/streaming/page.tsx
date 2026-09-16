import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";

import { StreamingSettingsForm } from "@/app/components/StreamingSettingsForm";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";
import { fetchStreamingSettings } from "@/lib/streaming-api";

export const metadata: Metadata = { title: "Where you watch · NextMovie" };

/**
 * Region and subscriptions.
 *
 * Until this page is used, every recommendation says a film is available
 * somewhere rather than available to you — the API has no way to tell the
 * difference between a service you pay for and one you have never heard of.
 */
export default async function StreamingSettingsPage() {
  const session = await getSession();

  if (!isSignedIn(session)) {
    redirect("/sign-in");
  }

  const outcome = await fetchStreamingSettings(session.accessToken);

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-1">
        <Link
          href="/profile"
          className="text-sm text-blue-700 hover:underline dark:text-blue-300"
        >
          ← Your profile
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">Where you watch</h1>
        <p className="max-w-prose text-sm text-neutral-600 dark:text-neutral-400">
          Tell us your country and what you subscribe to, and recommendations will say
          which films you can start watching tonight — and put them first.
        </p>
      </div>

      {outcome.ok ? (
        <StreamingSettingsForm
          region={outcome.settings.region}
          countries={outcome.settings.countries}
          services={outcome.settings.services}
        />
      ) : (
        // No redirect to /sign-in even on a 401: the cookie still says signed in,
        // so that page would bounce straight back and the two would loop.
        <p
          role="alert"
          className="rounded-md border border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-100"
        >
          {outcome.error.message}
        </p>
      )}
    </div>
  );
}
