"use client";

import { useEffect, type ReactNode } from "react";

// gh-#168 (T168), SPEC F102.7 — the composed /api/theme.css stylesheet degrading to the
// hardcoded shipped-default fallback already in globals.css is EXPECTED behaviour, not an error
// to hide from an operator. This component's only job is making that degraded path observable
// via a single console.warn, never re-fetching, never touching layout or paint. Renders nothing.

const THEME_STYLESHEET_HREF = "/api/theme.css";

// Same-origin, same-server request — if it hasn't settled (loaded OR errored) within this
// window it is treated as failed for observability purposes rather than waited on forever.
// Generous relative to a healthy same-origin response so a merely-slow request never
// false-positives.
const SETTLE_TIMEOUT_MS = 5000;

function warnDegraded(): void {
  console.warn(
    "[genwave] composed /api/theme.css failed to load — falling back to the shipped default globals.css tokens (SPEC F102.7)"
  );
}

/**
 * Watches the theme `<link>` root layout (`app/layout.tsx`) renders and warns once if it never
 * resolves into a real stylesheet.
 *
 * A plain `link.addEventListener("error", ...)` attached only after mount is not enough on its
 * own: `app/layout.tsx`'s `<link rel="stylesheet" href="/api/theme.css" precedence="theme">` is
 * parsed and its request kicked off well before this component's effect ever runs (client
 * hydration happens after the initial HTML — including this link — has already been parsed), so
 * a fast failure (e.g. a 404) can dispatch `error` before any listener here exists to catch it.
 *
 * Instead this checks `link.sheet` — non-null once the browser has actually parsed a stylesheet
 * from the response, `null` for both "still loading" and "already failed" — which is a stable
 * signal to inspect post-mount regardless of when the failure happened. An `error` listener is
 * still attached for the case where the failure happens AFTER mount (fires immediately, no
 * waiting); a bounded settle-timeout covers the case where it failed (or the request simply
 * never resolves) BEFORE mount, since no further `error` event will ever come for that.
 */
export function ThemeStylesheetWatchdog(): ReactNode {
  useEffect(() => {
    const link = document.head.querySelector<HTMLLinkElement>(
      `link[rel="stylesheet"][href="${THEME_STYLESHEET_HREF}"]`
    );
    if (!link) return;

    if (link.sheet !== null) return; // already loaded successfully by the time this mounted

    let warned = false;
    function reportOnce(): void {
      if (warned) return;
      warned = true;
      warnDegraded();
    }

    function handleError(): void {
      reportOnce();
    }

    link.addEventListener("error", handleError);
    const timer = window.setTimeout(() => {
      if (link.sheet === null) reportOnce();
    }, SETTLE_TIMEOUT_MS);

    return () => {
      link.removeEventListener("error", handleError);
      window.clearTimeout(timer);
    };
  }, []);

  return null;
}
