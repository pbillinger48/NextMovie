import type { AuthenticationResponse } from "@nextmovie/api-client";
import type { SessionOptions } from "iron-session";

/**
 * The browser's session, per ADR-0004.
 *
 * The API is bearer-token based because mobile requires it. The browser never
 * sees a token: this object is sealed into an encrypted, httpOnly cookie that
 * only the Next.js tier can open, so cross-site scripting anywhere on the page —
 * including from a transitive dependency — cannot read the session and replay it
 * elsewhere.
 *
 * Every field is optional because an unsigned-in visitor has an empty session
 * object rather than none at all. Use {@link isSignedIn} rather than testing
 * fields individually.
 */
export type SessionData = {
  accessToken?: string;
  /** ISO 8601, straight from the API, so the middleware can refresh before it lapses. */
  accessTokenExpiresAt?: string;
  refreshToken?: string;
  user?: {
    id: string;
    email: string;
    displayName: string;
  };
};

/** A session with everything needed to call the API on the user's behalf. */
export type ActiveSession = Required<Pick<
  SessionData,
  "accessToken" | "accessTokenExpiresAt" | "refreshToken" | "user"
>>;

export function isSignedIn(session: SessionData): session is SessionData & ActiveSession {
  return (
    session.accessToken !== undefined &&
    session.accessTokenExpiresAt !== undefined &&
    session.refreshToken !== undefined &&
    session.user !== undefined
  );
}

/**
 * Copies a freshly issued session from the API into the cookie's contents.
 *
 * One definition, used by sign-in, sign-up and the proxy's refresh, so a field
 * added to the session cannot be stored by one path and forgotten by another.
 * The caller still has to `save()`.
 */
export function applyAuthentication(
  session: SessionData,
  authentication: AuthenticationResponse,
): void {
  session.accessToken = authentication.accessToken;
  session.accessTokenExpiresAt = authentication.accessTokenExpiresAt;
  session.refreshToken = authentication.refreshToken;
  session.user = {
    id: authentication.user.id,
    email: authentication.user.email,
    displayName: authentication.user.displayName,
  };
}

/**
 * How long before expiry the proxy refreshes.
 *
 * Wide enough that a request is never served with a token about to lapse
 * mid-flight, narrow enough that ordinary browsing does not rotate the refresh
 * token constantly — every rotation is a write, and a replay of a rotated token
 * ends the session.
 */
export const REFRESH_THRESHOLD_MS = 60_000;

/**
 * Whether the access token is close enough to expiry to be worth replacing.
 *
 * Pure, and takes `now`, so the boundary is testable without waiting fifteen
 * minutes or stubbing the clock.
 */
export function needsRefresh(
  accessTokenExpiresAt: string,
  now: number,
  thresholdMs: number = REFRESH_THRESHOLD_MS,
): boolean {
  const expiresAt = Date.parse(accessTokenExpiresAt);

  // An unparseable expiry means the cookie is malformed or from an older
  // format. Refreshing is the safe reading: the worst case is one unnecessary
  // rotation, where trusting it could mean sending a long-dead token.
  if (Number.isNaN(expiresAt)) {
    return true;
  }

  return expiresAt - now <= thresholdMs;
}

/**
 * Reads the cookie password, failing loudly rather than starting insecurely.
 *
 * Next has no startup validation hook, so this runs on first use. An app that
 * booted with no password and only failed at sign-in would be far harder to
 * diagnose than one that says exactly what is missing.
 */
function sessionPassword(): string {
  const password = process.env.SESSION_COOKIE_PASSWORD;

  if (password === undefined || password.length < 32) {
    throw new Error(
      "SESSION_COOKIE_PASSWORD must be set to at least 32 characters. " +
        "Generate one with: openssl rand -base64 32",
    );
  }

  return password;
}

export const sessionOptions: SessionOptions = {
  get password() {
    // A getter, so a missing password throws when a request actually needs the
    // session rather than at module load — which would take down every page,
    // including the ones that never touch a session.
    return sessionPassword();
  },
  cookieName: "nextmovie_session",

  // Matched to the refresh token's 30 days. A cookie outliving the token it
  // carries would leave the user apparently signed in until their first call
  // failed.
  ttl: 60 * 60 * 24 * 30,

  cookieOptions: {
    // The whole point: JavaScript in the page cannot read this, so XSS cannot
    // exfiltrate the session.
    httpOnly: true,

    // ADR-0004's CSRF defence. Lax still allows normal top-level navigation
    // into the site while blocking the cross-site POSTs that CSRF relies on.
    // Server Actions add same-origin verification on top of this.
    sameSite: "lax",

    // Off in development only, because local Next runs over plain http and a
    // Secure cookie would simply never be stored. Anywhere else this is on.
    secure: process.env.NODE_ENV === "production",

    path: "/",
  },
};
