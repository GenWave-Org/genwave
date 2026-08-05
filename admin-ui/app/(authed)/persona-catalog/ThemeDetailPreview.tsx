"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { readErrorMessage } from "@/lib/problem-details";
import { prettifySlug } from "./format-slug";
import { THEME_PREVIEW_CONTAINER_CLASS } from "./theme-preview";

type PreviewState = { kind: "loading" } | { kind: "loaded"; css: string } | { kind: "error"; message: string };

export interface ThemeDetailPreviewProps {
  /** The theme's catalog slug — used only to caption the mock. */
  slug: string;
  /** The raw, already hash-verified theme manifest JSON text (SPEC F90.3) — the exact bytes
   * `ThemeInstallModal` later POSTs unchanged on confirm. POSTed here to `POST /api/themes/preview`
   * to be composed server-side; never parsed or composed on this side of the wire. */
  manifestText: string;
}

/**
 * The theme catalog's detail live-preview (SPEC F103.5, STORY-274, PLAN T186): opening a theme's
 * detail shows a REAL, composed mini-preview, scoped to its own container — never `:root` — so
 * nothing this renders can leak onto the admin page's own active theme.
 * `POST /api/themes/preview` (`ThemePreviewController`) runs the manifest through the SAME
 * `ThemeCssComposer` the live `/spectator/theme.css`/`/api/theme.css` routes call (just the scoped
 * overload) — this component only injects the returned CSS text into a same-origin `<style>`
 * element (admin-ui ships no CSP, so this is not a `style-src` concern the way the live sheets'
 * own "never inline" rule is) and paints a small mock with the app's OWN semantic-token utility
 * classes (`bg-bg`, `text-ink`, `bg-accent`, …). Those classes resolve to `var(--bg)`/`var(--ink)`/…
 * — the SAME custom properties the injected stylesheet sets on this container — so the mock renders
 * in the PREVIEWED theme's own colours and fonts with no bespoke rendering logic of its own; nothing
 * here re-implements composition in TypeScript. Because v1 themes reference only the
 * already-loaded curated fonts (SPEC F103.5/F103.10), opening this preview — even repeatedly —
 * never triggers a new font request.
 */
export function ThemeDetailPreview({ slug, manifestText }: ThemeDetailPreviewProps): ReactNode {
  const [state, setState] = useState<PreviewState>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    setState({ kind: "loading" });

    (async () => {
      try {
        const resp = await fetch("/api/themes/preview", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: manifestText,
        });
        if (cancelled) return;

        if (!resp.ok) {
          const message = await readErrorMessage(resp);
          if (cancelled) return;
          setState({ kind: "error", message });
          return;
        }

        const css = await resp.text();
        if (cancelled) return;
        setState({ kind: "loaded", css });
      } catch {
        if (cancelled) return;
        setState({ kind: "error", message: "Network error — check your connection" });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [manifestText]);

  if (state.kind === "loading") {
    return (
      <div className="space-y-2">
        <Skeleton className="h-24 w-full" />
      </div>
    );
  }

  if (state.kind === "error") {
    return (
      <p role="alert" className="text-[0.85rem] text-danger">
        {state.message}
      </p>
    );
  }

  return (
    <div
      data-testid="theme-live-preview"
      className={`${THEME_PREVIEW_CONTAINER_CLASS} rounded-[6px] border border-line bg-bg p-4`}
    >
      {/* The composed preview CSS (SPEC F103.5) — a same-origin <style> element, scoped entirely by
          its own selectors (see ThemePreviewController's own remarks); a plain text child, React's
          default escaping, never dangerouslySetInnerHTML. admin-ui ships no CSP today (gh-#346
          tracks adding one) — a future `style-src` without 'unsafe-inline' would need this inline
          text child replaced with a constructible stylesheet instead
          (`new CSSStyleSheet().replaceSync(state.css)` adopted via `adoptedStyleSheets`, which CSP
          never gates), not a nonce, since `state.css` changes every render. Leaving this note at
          the call site so gh-#346 finds it. */}
      <style>{state.css}</style>
      <p className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</p>
      <p className="mt-1 font-sans text-[0.85rem] text-mute">A live preview of this theme&apos;s palette and type.</p>
      <span className="mt-3 inline-flex w-fit items-center rounded-[999px] bg-accent px-2 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-ink">
        On Air
      </span>
    </div>
  );
}
