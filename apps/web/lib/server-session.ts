import "server-only";

import { getIronSession, type IronSession } from "iron-session";
import { cookies } from "next/headers";

import { sessionOptions, type SessionData } from "./session";

/**
 * The current request's session, for server components and server actions.
 *
 * Separate from `session.ts` because this reaches for `next/headers`, which the
 * middleware runtime cannot use — middleware opens the same cookie through the
 * request/response pair instead. Keeping the shape and options in a module that
 * imports neither is what lets both sides share them.
 *
 * Server components may read this. Only server actions and route handlers may
 * `save()` or `destroy()` it: writing a cookie during render is not something
 * the App Router permits.
 */
export function getSession(): Promise<IronSession<SessionData>> {
  return cookies().then((cookieStore) =>
    getIronSession<SessionData>(cookieStore, sessionOptions),
  );
}
