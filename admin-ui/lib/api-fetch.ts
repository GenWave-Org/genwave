// One wrapper that owns the 401 -> sign-out contract (SPEC F208.1, STORY-475, PLAN T566,
// gh-#729). Every client-side fetch call that carries the session cookie goes through here
// instead of a bare `fetch`: a 401 means the cookie went stale (expired session, revoked token,
// server restart), and the wrong place to relearn that is five call sites each toasting their own
// "unauthorized" message. `isUnauthorizedStatus` below is the only other exported way to test the
// `401` literal (about/page.tsx, a Server Component, uses it instead of holding the literal
// itself) — this file is the one place in admin-ui left holding it
// (__specs__/api-fetch-401.spec.ts's source scan, AC4, enforces that).
//
// Must stay importable from a Server Component: no top-level `window`/`document` access. `fetch`
// is a global in both worlds, so `clearSessionCookie` below is safe at any scope; only
// `defaultNavigate` reaches for `window`, and it does so from inside a function body — never
// evaluated at module-load time — so importing this module server-side never touches it.

/** Injectable seam over `window.location.assign` (STORY-475 ruling #2). Production callers never
 * set this — every real `apiFetch` call falls through to the default below. jsdom does not
 * implement `location.assign`, so a spec MUST inject a fake here rather than let a 401 reach it. */
export type NavigateFn = (url: string) => void;

function defaultNavigate(url: string): void {
  window.location.assign(url);
}

export interface ApiFetchOptions extends RequestInit {
  /** Test seam only (STORY-475 ruling #2) — production call sites never set this. */
  navigate?: NavigateFn;
}

/** `/login?expired=1` — the one URL every 401 path (this module's own `navigate`, and
 * app/session-expired/route.ts's redirect) lands the browser on. Exported so route.ts doesn't
 * hold its own copy of the literal. */
export const LOGIN_EXPIRED_PATH = "/login?expired=1";

/** `genwave-auth` — the HttpOnly session cookie's name. Exported so the one other place that
 * clears it (app/session-expired/route.ts's route handler) doesn't hold its own copy. */
export const SESSION_COOKIE = "genwave-auth";

/** The route a Server Component's 401 hands off to (STORY-475 ruling #5) — a route handler can
 * clear the cookie; a Server Component render can't. Exported so about/page.tsx's `redirect()`
 * call doesn't repeat the path as a bare string. */
export const SESSION_EXPIRED_PATH = "/session-expired";

/** Best-effort session-cookie clear (STORY-475 ruling #1). `genwave-auth` is HttpOnly, so client
 * JS can't delete it directly — POST /api/auth/logout (`AuthController.Logout`, `[AllowAnonymous]`)
 * runs ASP.NET's `SignOutAsync`, and its expiring `Set-Cookie` reaches the browser through the
 * `/api/:path*` same-origin rewrite exactly like every other `/api/*` call in this directory (a
 * direct browser fetch, unlike `app/login/actions.ts`'s server-side `logout()` action, which has
 * to forward `Set-Cookie` by hand because IT runs on the server). A failed POST is swallowed — the
 * operator is leaving for /login either way, and a cookie the browser still holds doesn't stop the
 * login page from working. */
async function clearSessionCookie(): Promise<void> {
  try {
    await fetch("/api/auth/logout", { method: "POST", credentials: "include" });
  } catch {
    // best-effort — navigate regardless (ruling #1)
  }
}

/** True for the one status this module owns — the only place in admin-ui allowed to hold the
 * `401` literal (SPEC F208.1). A caller that needs to special-case a 401 outside a plain
 * `apiFetch` call (about/page.tsx's Server Component render, which can't itself clear a cookie or
 * call `apiFetch`) tests through this instead of repeating the literal itself. */
export function isUnauthorizedStatus(status: number): boolean {
  return status === 401;
}

/** True when `err` is the failed `Response` {@link apiFetch} rejects a non-2xx, non-401 call with
 * (AC6) — as opposed to a thrown network error, which propagates unchanged from the underlying
 * `fetch` and carries no `status`. Every migrated call site's catch block uses this to tell the
 * two apart and classify exactly as it did before this module owned the fetch. */
export function isApiFetchResponseError(err: unknown): err is Response {
  return typeof err === "object" && err !== null && "status" in err;
}

/**
 * Fetch wrapper every session-cookie-carrying client call should use instead of bare `fetch`
 * (SPEC F208.1). Resolves with `response` for a 2xx. For any other non-401 status it REJECTS with
 * `response` (AC6) — a caller that used to branch on `response.ok` now catches instead, reading
 * `.status` off whatever it caught (via {@link isApiFetchResponseError}) to classify the failure
 * exactly as before. A network failure (no response at all) still rejects exactly as `fetch` does
 * — it never satisfies {@link isApiFetchResponseError}, so a caller's classification is unchanged.
 *
 * A 401 is handled here and only here: clear the session cookie, then hard-navigate to
 * `/login?expired=1`. The returned promise never settles in that case (ruling #3) — the page is on
 * its way out, so there is nothing for a caller to do with a resolved/rejected value, and letting
 * the promise settle risks an error toast flickering on screen for the instant before the
 * navigation completes.
 */
export async function apiFetch(
  input: RequestInfo | URL,
  init: ApiFetchOptions = {}
): Promise<Response> {
  const { navigate = defaultNavigate, ...requestInit } = init;
  const response = await fetch(input, requestInit);
  if (response.ok) return response;

  if (isUnauthorizedStatus(response.status)) {
    await clearSessionCookie();
    navigate(LOGIN_EXPIRED_PATH);
    // Never settles — see the doc comment above (ruling #3).
    return new Promise<Response>(() => {
      /* the page is navigating away; nothing ever resolves or rejects this */
    });
  }

  throw response;
}
