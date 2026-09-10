import Link from "next/link";

/**
 * Starts the Google sign-in flow.
 *
 * A link, not a button with an action: the flow begins with a top-level
 * navigation to Google, and the route handler it points at is a plain redirect.
 * Nothing here is state-changing on our side, so a GET is honest.
 */
export function GoogleSignInButton({ label }: { label: string }) {
  return (
    <Link
      href="/api/auth/google/start"
      className="flex w-full items-center justify-center gap-3 rounded-md border border-neutral-300 bg-white px-4 py-2 text-base font-medium text-neutral-900 shadow-sm hover:bg-neutral-50 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-blue-600 dark:border-neutral-700 dark:bg-neutral-900 dark:text-neutral-100 dark:hover:bg-neutral-800"
    >
      <GoogleMark />
      {label}
    </Link>
  );
}

/** Google's mark, inlined so the button does not depend on a third-party asset. */
function GoogleMark() {
  return (
    <svg aria-hidden="true" width="18" height="18" viewBox="0 0 18 18">
      <path
        fill="#4285F4"
        d="M17.64 9.2c0-.64-.06-1.25-.16-1.84H9v3.48h4.84a4.14 4.14 0 0 1-1.8 2.72v2.26h2.92c1.7-1.57 2.68-3.88 2.68-6.62Z"
      />
      <path
        fill="#34A853"
        d="M9 18c2.43 0 4.47-.8 5.96-2.18l-2.92-2.26c-.8.54-1.84.86-3.04.86-2.34 0-4.32-1.58-5.03-3.7H.96v2.33A9 9 0 0 0 9 18Z"
      />
      <path
        fill="#FBBC05"
        d="M3.97 10.72a5.4 5.4 0 0 1 0-3.44V4.95H.96a9 9 0 0 0 0 8.1l3.01-2.33Z"
      />
      <path
        fill="#EA4335"
        d="M9 3.58c1.32 0 2.5.45 3.44 1.35l2.58-2.58C13.46.89 11.43 0 9 0A9 9 0 0 0 .96 4.95l3.01 2.33C4.68 5.16 6.66 3.58 9 3.58Z"
      />
    </svg>
  );
}
