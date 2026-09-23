import { NextResponse } from "next/server";
import { LOGIN_EXPIRED_PATH, SESSION_COOKIE } from "@/lib/api-fetch";

// Route handler (STORY-475 ruling #5) a server-rendered 401 (about/page.tsx) redirects to — a
// Server Component render can't set/delete a cookie itself (see next/headers' `cookies()` docs:
// `.set`/`.delete` only work in a Server Function or Route Handler). Deliberately NOT under
// `/api` — that prefix is rewritten to the C# backend (next.config.ts), so a route handler there
// would never run. Deletes the stale session cookie and lands on the same `LOGIN_EXPIRED_PATH` the
// client-side `apiFetch` wrapper (`lib/api-fetch.ts`) uses (SPEC F208.1) — both constants are
// imported from there, not repeated here.
//
// A relative Location, not `new URL(path, request.url)`: in the standalone image that resolves
// against the 0.0.0.0 bind address, not the browser's Host. The browser resolves this one against
// the origin it is already on.

export function GET(): NextResponse {
  const response = new NextResponse(null, {
    status: 307,
    headers: { Location: LOGIN_EXPIRED_PATH },
  });
  response.cookies.set(SESSION_COOKIE, "", { path: "/", maxAge: 0 });
  return response;
}
