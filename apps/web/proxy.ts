import { getIronSession } from "iron-session";
import { NextResponse, type NextRequest } from "next/server";

import { refresh } from "@/lib/auth-api";
import { applyAuthentication, needsRefresh, sessionOptions, type SessionData } from "@/lib/session";

/**
 * Refreshes the access token before it expires, invisibly to the browser.
 *
 * This runs here — `proxy.ts`, formerly `middleware.ts`, deprecated in Next 16 —
 * because it is the only place that both sees every request and may write a
 * cookie. Server components may read the session but the App Router will not let
 * them set one, so a page that noticed an expired token mid-render would have
 * nowhere to put its replacement.
 *
 * Refresh is not idempotent: ADR-0003 rotates the refresh token on every use and
 * treats a replayed one as theft, revoking the whole session. Two refreshes
 * racing therefore sign the user out. The matcher below excludes prefetches for
 * exactly that reason — Next prefetches `<Link>` targets eagerly, and a prefetch
 * quietly rotating the session's token would be a self-inflicted logout. The
 * headers cannot be checked in code here: Next strips its Flight headers from
 * `request` inside proxy, so the exclusion has to be expressed in the matcher.
 *
 * A residual race remains for two genuine navigations in the same instant, from
 * two tabs or a double-click. It recovers by signing in again, and the durable
 * fix is a short grace window on a just-rotated token — an amendment to
 * ADR-0003, not something to slip in here.
 */
export async function proxy(request: NextRequest) {
  // iron-session writes its Set-Cookie onto this response. It is not what gets
  // returned — see handOn.
  const sealed = NextResponse.next();
  const session = await getIronSession<SessionData>(request, sealed, sessionOptions);

  if (session.refreshToken === undefined || session.accessTokenExpiresAt === undefined) {
    // Not signed in. Nothing to refresh, and no redirect either — pages decide
    // for themselves what an anonymous visitor sees.
    return sealed;
  }

  if (!needsRefresh(session.accessTokenExpiresAt, Date.now())) {
    return sealed;
  }

  const refreshed = await refresh(session.refreshToken);

  if (refreshed.ok) {
    applyAuthentication(session, refreshed.session);
    await session.save();

    return handOn(request, sealed);
  }

  if (refreshed.error.kind === "unreachable") {
    // The API being down must not sign everybody out. The stored tokens are
    // still valid; the next request can try again.
    return sealed;
  }

  // The refresh token was rejected: expired, revoked, or replayed. The session
  // is genuinely over, and keeping a dead cookie would leave the UI claiming
  // the user is signed in while every call fails.
  session.destroy();

  return handOn(request, sealed);
}

/**
 * Hands the rewritten session on to the render, as well as back to the browser.
 *
 * Writing a cookie on the response only tells the *browser* about the change.
 * The server components that render this same request still read the cookie the
 * browser sent, so without this the request that triggered a refresh would
 * render with the token we just replaced — and a destroyed session would render
 * as signed in one last time.
 *
 * Rewriting the request's own cookie header, and passing those headers through
 * `NextResponse.next({ request })`, is the documented way to make a proxy's
 * change visible upstream.
 */
function handOn(request: NextRequest, sealed: NextResponse): NextResponse {
  // Read the raw Set-Cookie headers rather than `sealed.cookies`: iron-session
  // appends the header itself, and it does not show up through NextResponse's
  // cookie API. Reading the wrong one is silent — every session looks destroyed
  // — so this is deliberate rather than stylistic.
  const setCookies = sealed.headers.getSetCookie();
  const sessionValue = sessionCookieValue(setCookies);

  if (sessionValue === undefined || sessionValue === "") {
    // Destroyed: iron-session clears it by setting an empty value, which would
    // not unseal anyway — but removing it outright is unambiguous.
    request.cookies.delete(sessionOptions.cookieName);
  } else {
    request.cookies.set(sessionOptions.cookieName, sessionValue);
  }

  const response = NextResponse.next({ request: { headers: request.headers } });

  // Carry the Set-Cookie headers across to the response actually returned, so
  // the browser is told about the change as well as the render.
  for (const setCookie of setCookies) {
    response.headers.append("set-cookie", setCookie);
  }

  return response;
}

/** The sealed session value out of a set of Set-Cookie headers, if present. */
function sessionCookieValue(setCookies: string[]): string | undefined {
  const prefix = `${sessionOptions.cookieName}=`;
  const header = setCookies.find((candidate) => candidate.startsWith(prefix));

  if (header === undefined) {
    return undefined;
  }

  return decodeURIComponent(header.slice(prefix.length).split(";", 1)[0]);
}

export const config = {
  matcher: [
    {
      /*
       * Everything except API routes, build output and metadata files.
       */
      source: "/((?!api|_next/static|_next/image|favicon.ico|sitemap.xml|robots.txt).*)",

      /*
       * ...and not prefetches. `missing` matches only when every listed header
       * is absent, which is what keeps a speculative prefetch from rotating a
       * refresh token that the navigation it anticipates will then need.
       */
      missing: [
        { type: "header", key: "next-router-prefetch" },
        { type: "header", key: "purpose", value: "prefetch" },
      ],
    },
  ],
};
