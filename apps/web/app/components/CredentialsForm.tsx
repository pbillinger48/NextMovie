"use client";

import Link from "next/link";
import { useActionState } from "react";

import type { AuthFormState } from "@/app/actions/auth";

type CredentialsFormProps = {
  action: (state: AuthFormState | undefined, formData: FormData) => Promise<AuthFormState>;
  /** Sign-up collects a display name; sign-in does not. */
  withDisplayName?: boolean;
  submitLabel: string;
  alternative: { prompt: string; href: string; label: string };
};

/**
 * Email and password form for signing in and signing up.
 *
 * A client component only because `useActionState` needs one — the submission
 * itself runs on the server, and the form still works without JavaScript. The
 * password is never echoed back into the DOM on a failed attempt, only the
 * fields that are safe to restore.
 */
export function CredentialsForm({
  action,
  withDisplayName = false,
  submitLabel,
  alternative,
}: CredentialsFormProps) {
  const [state, submit, pending] = useActionState(action, undefined);

  return (
    <form action={submit} className="flex w-full max-w-sm flex-col gap-4">
      {state?.message ? (
        <p
          role="alert"
          className="rounded-md border border-red-300 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950 dark:text-red-100"
        >
          {state.message}
        </p>
      ) : null}

      <Field
        id="email"
        label="Email"
        type="email"
        autoComplete="email"
        defaultValue={state?.values?.email}
        errors={state?.fieldErrors?.Email}
        required
      />

      {withDisplayName ? (
        <Field
          id="displayName"
          label="Display name"
          type="text"
          autoComplete="name"
          defaultValue={state?.values?.displayName}
          errors={state?.fieldErrors?.DisplayName}
          required
        />
      ) : null}

      <Field
        id="password"
        label="Password"
        type="password"
        // new-password on sign-up prompts password managers to offer a generated
        // one; current-password on sign-in asks them to fill the stored one.
        autoComplete={withDisplayName ? "new-password" : "current-password"}
        errors={state?.fieldErrors?.Password}
        required
      />

      <button
        type="submit"
        disabled={pending}
        className="rounded-md bg-blue-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-blue-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 disabled:opacity-60"
      >
        {pending ? "Working…" : submitLabel}
      </button>

      <p className="text-sm text-neutral-600 dark:text-neutral-400">
        {alternative.prompt}{" "}
        <Link
          href={alternative.href}
          className="underline underline-offset-2 hover:text-neutral-900 dark:hover:text-neutral-100"
        >
          {alternative.label}
        </Link>
      </p>
    </form>
  );
}

type FieldProps = {
  id: string;
  label: string;
  type: string;
  autoComplete: string;
  defaultValue?: string;
  errors?: string[];
  required?: boolean;
};

function Field({ id, label, type, autoComplete, defaultValue, errors, required }: FieldProps) {
  const errorId = `${id}-error`;

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-sm font-medium">
        {label}
      </label>

      <input
        id={id}
        name={id}
        type={type}
        autoComplete={autoComplete}
        defaultValue={defaultValue}
        required={required}
        aria-describedby={errors ? errorId : undefined}
        aria-invalid={errors ? true : undefined}
        className="rounded-md border border-neutral-300 bg-white px-3 py-2 text-base shadow-sm focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-700 dark:bg-neutral-900"
      />

      {errors ? (
        <p id={errorId} className="text-sm text-red-700 dark:text-red-300">
          {errors.join(" ")}
        </p>
      ) : null}
    </div>
  );
}
