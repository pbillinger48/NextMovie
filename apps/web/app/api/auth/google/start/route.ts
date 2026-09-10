import { NextResponse } from "next/server";

import {
  PENDING_FLOW_COOKIE,
  PENDING_FLOW_TTL_SECONDS,
  beginFlow,
  googleConfig,
  sealPendingFlow,
} from "@/lib/google-oauth";

/**
 * Sends the browser to Google to sign in.
 *
 * A route handler rather than a server action because this ends in a redirect to
 * a third party, which is a plain top-level navigation — the browser follows it,
 * and Google needs somewhere to send the user back to.
 */
export async function GET() {
  const config = googleConfig();
  const { authorizeUrl, pending } = beginFlow(config);

  const response = NextResponse.redirect(authorizeUrl);

  // The state, PKCE verifier and nonce go into an encrypted cookie rather than
  // server memory: nothing here is sticky, and the callback may land on a
  // different instance from the one that started the flow.
  //
  // SameSite=Lax rather than Strict on purpose. The callback arrives as a
  // top-level navigation from accounts.google.com, and Strict would withhold the
  // cookie on exactly that request — the flow would fail every time.
  response.cookies.set(PENDING_FLOW_COOKIE, await sealPendingFlow(pending), {
    httpOnly: true,
    sameSite: "lax",
    secure: process.env.NODE_ENV === "production",
    path: "/",
    maxAge: PENDING_FLOW_TTL_SECONDS,
  });

  return response;
}
