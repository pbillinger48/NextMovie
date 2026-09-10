import { createHash } from "node:crypto";

import { sealData, unsealData } from "iron-session";

/**
 * The browser-facing half of Google sign-in, per ADR-0005.
 *
 * Next runs the authorization-code flow with PKCE and ends up holding Google's
 * ID token, which it hands to the API. The API verifies it and issues our own
 * tokens; the browser never sees any of them.
 *
 * The client secret lives here rather than in the API deliberately — this tier is
 * the confidential OAuth client, and the API's ADR-0005 job is verifying an ID
 * token, not redeeming codes.
 */

const AUTHORIZE_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth";
const TOKEN_ENDPOINT = "https://oauth2.googleapis.com/token";

/** Holds the in-flight flow's secrets between the redirect out and the callback back. */
export const PENDING_FLOW_COOKIE = "nextmovie_google_flow";

/**
 * Ten minutes: long enough to sign in and pick an account, short enough that an
 * abandoned flow cannot be resumed much later.
 */
export const PENDING_FLOW_TTL_SECONDS = 600;

export type PendingFlow = {
  /** Echoed by Google and compared on return — the CSRF defence for the callback. */
  state: string;
  /** The PKCE secret. Only its hash left this server. */
  codeVerifier: string;
  /** Bound into the ID token, so a token from another flow cannot be substituted. */
  nonce: string;
};

type GoogleConfig = {
  clientId: string;
  clientSecret: string;
  redirectUri: string;
};

/**
 * Reads Google's client configuration, failing loudly when it is missing.
 *
 * The redirect URI comes from configuration rather than from the request's Host
 * header: trusting the header would let a spoofed host redirect the flow
 * somewhere else, and Google requires an exact registered match anyway.
 */
export function googleConfig(): GoogleConfig {
  const clientId = process.env.GOOGLE_CLIENT_ID;
  const clientSecret = process.env.GOOGLE_CLIENT_SECRET;

  if (!clientId || !clientSecret) {
    throw new Error(
      "GOOGLE_CLIENT_ID and GOOGLE_CLIENT_SECRET must be set to use Google sign-in. " +
        "Create an OAuth client at https://console.cloud.google.com/apis/credentials",
    );
  }

  return {
    clientId,
    clientSecret,
    redirectUri:
      process.env.GOOGLE_REDIRECT_URI ?? "http://localhost:3000/api/auth/google/callback",
  };
}

/** Starts a flow: the URL to send the browser to, and the secrets to remember. */
export function beginFlow(config: GoogleConfig): {
  authorizeUrl: string;
  pending: PendingFlow;
} {
  const pending: PendingFlow = {
    state: randomUrlSafe(),
    codeVerifier: randomUrlSafe(),
    nonce: randomUrlSafe(),
  };

  const parameters = new URLSearchParams({
    client_id: config.clientId,
    redirect_uri: config.redirectUri,
    response_type: "code",
    scope: "openid email profile",
    state: pending.state,
    nonce: pending.nonce,
    code_challenge: challengeFor(pending.codeVerifier),
    code_challenge_method: "S256",

    // Ask Google which account to use rather than silently reusing the one the
    // browser happens to be signed into. Someone signing in on a shared machine
    // should not be handed a stranger's account.
    prompt: "select_account",
  });

  return { authorizeUrl: `${AUTHORIZE_ENDPOINT}?${parameters}`, pending };
}

/**
 * Redeems the authorization code for an ID token.
 *
 * @returns The ID token, or null if Google refused the exchange.
 */
export async function exchangeCode(
  config: GoogleConfig,
  code: string,
  codeVerifier: string,
): Promise<string | null> {
  let response: Response;

  try {
    response = await fetch(TOKEN_ENDPOINT, {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        client_id: config.clientId,
        client_secret: config.clientSecret,
        redirect_uri: config.redirectUri,
        grant_type: "authorization_code",
        code,
        code_verifier: codeVerifier,
      }),
    });
  } catch {
    return null;
  }

  if (!response.ok) {
    return null;
  }

  const body: unknown = await response.json();

  if (body && typeof body === "object" && "id_token" in body) {
    const { id_token: idToken } = body as { id_token?: unknown };

    return typeof idToken === "string" ? idToken : null;
  }

  return null;
}

/**
 * Reads the `nonce` out of an ID token without verifying its signature.
 *
 * Safe only because of where this token came from: straight from Google's token
 * endpoint over TLS, in response to a code we just redeemed. Nothing is trusted
 * on the strength of this read — the token's signature is verified by the API
 * before a single claim in it is acted on. This only confirms the token belongs
 * to the flow we started.
 */
export function idTokenNonce(idToken: string): string | null {
  const payload = idToken.split(".")[1];

  if (payload === undefined) {
    return null;
  }

  try {
    const claims: unknown = JSON.parse(Buffer.from(payload, "base64url").toString("utf8"));

    if (claims && typeof claims === "object" && "nonce" in claims) {
      const { nonce } = claims as { nonce?: unknown };

      return typeof nonce === "string" ? nonce : null;
    }
  } catch {
    return null;
  }

  return null;
}

export function sealPendingFlow(pending: PendingFlow): Promise<string> {
  return sealData(pending, { password: flowPassword(), ttl: PENDING_FLOW_TTL_SECONDS });
}

export async function unsealPendingFlow(sealed: string): Promise<PendingFlow | null> {
  try {
    const pending = await unsealData<PendingFlow>(sealed, {
      password: flowPassword(),
      ttl: PENDING_FLOW_TTL_SECONDS,
    });

    return pending.state && pending.codeVerifier && pending.nonce ? pending : null;
  } catch {
    // Tampered with, or simply expired. Either way there is no flow to resume.
    return null;
  }
}

/**
 * Compares two secrets without leaking where they first differ.
 *
 * `===` on a state parameter returns as soon as it finds a mismatched character,
 * which in principle lets an attacker discover a valid state one character at a
 * time. The cost of not caring is small; the cost of caring is four lines.
 */
export function secretsMatch(a: string, b: string): boolean {
  if (a.length !== b.length) {
    return false;
  }

  let difference = 0;

  for (let i = 0; i < a.length; i++) {
    difference |= a.charCodeAt(i) ^ b.charCodeAt(i);
  }

  return difference === 0;
}

/** Reuses the session cookie's password: same tier, same trust boundary, one secret to rotate. */
function flowPassword(): string {
  const password = process.env.SESSION_COOKIE_PASSWORD;

  if (password === undefined || password.length < 32) {
    throw new Error("SESSION_COOKIE_PASSWORD must be set to at least 32 characters.");
  }

  return password;
}

function randomUrlSafe(bytes: number = 32): string {
  const buffer = new Uint8Array(bytes);
  crypto.getRandomValues(buffer);

  return Buffer.from(buffer).toString("base64url");
}

function challengeFor(codeVerifier: string): string {
  // Node's synchronous hash rather than crypto.subtle, which is async and would
  // make beginFlow async for no benefit. This module is imported only by route
  // handlers, which run on the Node runtime — never by the proxy or the browser.
  return createHash("sha256").update(codeVerifier).digest("base64url");
}
