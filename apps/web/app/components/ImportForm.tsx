"use client";

import { useActionState } from "react";

import { uploadExport } from "@/app/actions/import";

/**
 * Uploads a Letterboxd export.
 *
 * A client component only because `useActionState` needs one — the upload itself
 * runs on the server, and the form still submits without JavaScript.
 */
export function ImportForm() {
  const [state, submit, pending] = useActionState(uploadExport, undefined);

  return (
    <form action={submit} className="flex w-full max-w-lg flex-col gap-4">
      {state?.message ? (
        <p
          role="alert"
          className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
        >
          {state.message}
        </p>
      ) : null}

      <div className="flex flex-col gap-1">
        <label htmlFor="file" className="text-sm font-medium">
          Letterboxd export
        </label>

        <input
          id="file"
          name="file"
          type="file"
          accept=".csv,text/csv"
          required
          className="rounded-md border border-neutral-300 bg-white px-3 py-2 text-sm file:mr-3 file:rounded file:border-0 file:bg-neutral-100 file:px-3 file:py-1.5 file:text-sm dark:border-neutral-700 dark:bg-neutral-900 dark:file:bg-neutral-800"
        />

        <p className="text-sm text-neutral-600 dark:text-neutral-400">
          Upload <code>ratings.csv</code>, <code>watched.csv</code> or{" "}
          <code>diary.csv</code> from your Letterboxd export.
        </p>
      </div>

      <button
        type="submit"
        disabled={pending}
        className="self-start rounded-md bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 disabled:opacity-60"
      >
        {pending ? "Uploading…" : "Start import"}
      </button>
    </form>
  );
}
