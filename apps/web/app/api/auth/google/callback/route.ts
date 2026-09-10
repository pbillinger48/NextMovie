import { getIronSession } from "iron-session";
import { NextResponse, type NextRequest } from "next/server";

import { signInWithGoogle } from "@/lib/auth-api";
import {
  PENDING_FLOW_COOKIE,
  exchangeCode,
  googleConfig,
  idTokenNonce,
  secretsMatch,
  unsealPendingFlow,
} from "@/lib/google-oauth";
import { applyAuthentication, sessionOptions, type SessionData } from "@/lib/session";

/**
 * Where Google sends the browser back to.
 *
 * Redeems the authorization code for an ID token, hands that to the API, and
 * seals the session the API returns into the cookie. The browser is a passenger
 * throughout: it never sees the ID token, the client secret, or our own tokens.
 *
 * Every failure ends the same way — back at sign-in with a generic message. The
 * specifics are of no use to a legitimate user and of considerable use to
 * somebody probing the callback.
 */
export async function GET(request: NextRequest) {
  const parameters = request.nextUrl.searchParams;

  // Google reports a declined consent screen this way. Not an error worth
  // alarming anyone about — the user simply changed their mind.
  if (parameters.get("error") !== null) {
    return failed(request, "cancelled");
  }

  const code = parameters.get("code");
  const state = parameters.get("state");
  const sealedFlow = request.cookies.get(PENDING_FLOW_COOKIE)?.value;

  if (code === null || state === null || sealedFlow === undefined) {
    return failed(request);
  }

  const pending = await unsealPendingFlow(sealedFlow);

  // The state check is what stops an attacker feeding their own authorization
  // code to a signed-in victim's browser and quietly attaching their Google
  // account. Without it the callback will attach whatever it is handed.
  if (pending === null || !secretsMatch(pending.state, state)) {
    return failed(request);
  }

  const config = googleConfig();
  const idToken = await exchangeCode(config, code, pending.codeVerifier);

  if (idToken === null) {
    return failed(request);
  }

  // Binds the token to the flow this browser started, so one obtained elsewhere
  // cannot be swapped in. The signature is verified by the API, not here.
  const nonce = idTokenNonce(idToken);

  if (nonce === null || !secretsMatch(pending.nonce, nonce)) {
    return failed(request);
  }

  const outcome = await signInWithGoogle(idToken);

  if (!outcome.ok) {
    return failed(
      request,
      outcome.error.kind === "email-unverified" ? "unverified" : undefined,
    );
  }

  const response = NextResponse.redirect(new URL("/", request.nextUrl.origin));

  // Written onto this redirect directly rather than through next/headers, so the
  // session cookie and the redirect are unambiguously the same response.
  const session = await getIronSession<SessionData>(request, response, sessionOptions);
  applyAuthentication(session, outcome.session);
  await session.save();

  // The flow is over; its secrets have no further use and should not linger.
  response.cookies.delete(PENDING_FLOW_COOKIE);

  return response;
}

function failed(request: NextRequest, reason: string = "failed"): NextResponse {
  const signIn = new URL("/sign-in", request.nextUrl.origin);
  signIn.searchParams.set("google", reason);

  const response = NextResponse.redirect(signIn);
  response.cookies.delete(PENDING_FLOW_COOKIE);

  return response;
}
