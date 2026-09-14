import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";

import { ImportForm } from "@/app/components/ImportForm";
import { getSession } from "@/lib/server-session";
import { isSignedIn } from "@/lib/session";

export const metadata: Metadata = { title: "Import from Letterboxd · NextMovie" };

export default async function ImportPage() {
  if (!isSignedIn(await getSession())) {
    redirect("/sign-in");
  }

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">Import from Letterboxd</h1>
        <p className="max-w-prose text-sm text-neutral-600 dark:text-neutral-400">
          Letterboxd has no public API, so bring your data across with a CSV export.
          In Letterboxd, go to{" "}
          <Link
            href="https://letterboxd.com/settings/data/"
            target="_blank"
            rel="noopener noreferrer"
            className="underline underline-offset-2"
          >
            Settings → Data
          </Link>{" "}
          and choose Export Your Data.
        </p>
      </div>

      <ImportForm />

      <div className="max-w-prose text-sm text-neutral-600 dark:text-neutral-400">
        <h2 className="mb-1 font-medium text-neutral-900 dark:text-neutral-100">
          What happens next
        </h2>
        <p>
          Each film is matched against TMDb, which takes a minute or two for a large
          library. Most match automatically. A handful usually cannot be told apart
          from films with the same name and year — you will be asked about those
          rather than have us guess, because a wrong match is hard to notice and
          hard to undo.
        </p>
      </div>
    </div>
  );
}
