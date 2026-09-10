"use client";

import { useActionState } from "react";

import { saveProfile } from "@/app/actions/profile";

type ProfileFormProps = {
  displayName: string;
  profileImageUrl: string | null;
};

/**
 * Editable profile fields.
 *
 * Both fields are always submitted, because the API replaces the profile rather
 * than patching it — clearing the image field is how you remove an avatar, and
 * that only works if the empty value is actually sent.
 */
export function ProfileForm({ displayName, profileImageUrl }: ProfileFormProps) {
  const [state, submit, pending] = useActionState(saveProfile, undefined);

  return (
    <form action={submit} className="flex w-full max-w-md flex-col gap-4">
      {state?.message ? (
        <p
          role="alert"
          className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
        >
          {state.message}
        </p>
      ) : null}

      {state?.saved ? (
        // aria-live rather than role="alert": a save confirmation is useful to
        // hear but should not interrupt whatever the user is doing next.
        <p
          aria-live="polite"
          className="rounded-md border border-green-300 bg-green-50 px-4 py-3 text-sm text-green-900 dark:border-green-900 dark:bg-green-950 dark:text-green-100"
        >
          Profile saved.
        </p>
      ) : null}

      <div className="flex flex-col gap-1">
        <label htmlFor="displayName" className="text-sm font-medium">
          Display name
        </label>
        <input
          id="displayName"
          name="displayName"
          type="text"
          autoComplete="name"
          required
          maxLength={100}
          defaultValue={displayName}
          aria-describedby={state?.fieldErrors?.DisplayName ? "displayName-error" : undefined}
          aria-invalid={state?.fieldErrors?.DisplayName ? true : undefined}
          className="rounded-md border border-neutral-300 bg-white px-3 py-2 text-base shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-700 dark:bg-neutral-900"
        />
        {state?.fieldErrors?.DisplayName ? (
          <p id="displayName-error" className="text-sm text-red-700 dark:text-red-300">
            {state.fieldErrors.DisplayName.join(" ")}
          </p>
        ) : null}
      </div>

      <div className="flex flex-col gap-1">
        <label htmlFor="profileImageUrl" className="text-sm font-medium">
          Profile image URL
        </label>
        <input
          id="profileImageUrl"
          name="profileImageUrl"
          type="url"
          inputMode="url"
          placeholder="https://example.com/avatar.jpg"
          defaultValue={profileImageUrl ?? ""}
          aria-describedby={
            state?.fieldErrors?.ProfileImageUrl ? "profileImageUrl-error" : "profileImageUrl-hint"
          }
          aria-invalid={state?.fieldErrors?.ProfileImageUrl ? true : undefined}
          className="rounded-md border border-neutral-300 bg-white px-3 py-2 text-base shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-700 dark:bg-neutral-900"
        />
        {state?.fieldErrors?.ProfileImageUrl ? (
          <p id="profileImageUrl-error" className="text-sm text-red-700 dark:text-red-300">
            {state.fieldErrors.ProfileImageUrl.join(" ")}
          </p>
        ) : (
          <p id="profileImageUrl-hint" className="text-sm text-neutral-600 dark:text-neutral-400">
            Leave empty to remove your picture.
          </p>
        )}
      </div>

      <button
        type="submit"
        disabled={pending}
        className="self-start rounded-md bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 disabled:opacity-60"
      >
        {pending ? "Saving…" : "Save profile"}
      </button>
    </form>
  );
}
