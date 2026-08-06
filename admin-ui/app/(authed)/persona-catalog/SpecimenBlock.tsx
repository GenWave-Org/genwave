"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { readErrorMessage } from "@/lib/problem-details";

export interface SpecimenBlockProps {
  /** The font pack's catalog slug — used to build the asset route below. Already validated shape
   * (`CatalogIndexValidator.SlugSegment`) by the time it reaches this component, since it only
   * ever arrives via an already-fetched shelf/detail entry — never raw operator input. */
  slug: string;
  /** The pack's upright face filename (SPEC F104.4, `CatalogEntryResponse.FontSpecimenFile`,
   * T194), resolved server-side against this entry's OWN hash-verified `assets[]` list — or
   * `null` when no upright face resolves (a malformed manifest, or a non-font entry that somehow
   * reached this component, which never happens through `PersonaCatalogClient`'s own kind-routed
   * switch). `null` degrades to the same visible copy a fetch failure gets, never a crash. */
  specimenFile: string | null;
}

type SpecimenState =
  | { kind: "loading" }
  | { kind: "loaded"; localFamily: string }
  | { kind: "error"; message: string };

/**
 * The font pack detail's transient specimen block (SPEC F104.4, STORY-281, PLAN T202): loads the
 * pack's REAL upright face through the hash-verified asset proxy
 * (`GET /api/catalog/entries/{slug}/assets/{file}`, T194) and renders sample text SET IN that
 * face — "the specimen renders in the pack's actual face" (T202's own acceptance line), not merely
 * a family name printed in the app's own Source Sans.
 *
 * <b>Loading mechanism + auth (stated honestly, per this task's own instruction).</b> A plain
 * `fetch()` with no explicit `credentials` option — the SAME default `"same-origin"` behavior every
 * other request on this page already relies on (`loadDetail` above, `ThemeInstallModal`'s own
 * POST, …), so the session cookie rides this request exactly as it does theirs. The response is
 * read as a `Blob`, wrapped in a same-tab-only `URL.createObjectURL`, and fed to the CSS Font
 * Loading API (`new FontFace(...)` + `document.fonts.add`) rather than an injected `@font-face`
 * `<style>` rule pointing straight at the asset URL. Both shapes would carry the same auth cookie
 * (a raw `url(/api/catalog/...)` inside an injected stylesheet rides it exactly as a `fetch()` to
 * the same origin does) — `fetch()` is chosen anyway because it is the only one of the two that
 * hands back a real `Response`: the exact HTTP status (404/502/503) this component turns into the
 * visible degraded copy below. A bare CSS `url()` reference fails SILENTLY — the browser simply
 * never paints the face, with nothing this component could read to explain why — which cannot
 * satisfy SPEC F104.4/AC3's "visible degraded copy on integrity/connectivity failure" requirement.
 *
 * <b>Caching (also stated honestly).</b> `CatalogController.Asset` stamps `Cache-Control: no-store`
 * on this exact route (T194), so the browser's own HTTP cache never retains the bytes past this
 * one request; the `Blob`/object URL this component builds from them lives in memory only, scoped
 * to this component instance, and is revoked the moment it unmounts or `specimenFile` changes
 * (below). Nothing outlives the open detail panel; nothing is ever written to disk, the font cache,
 * or served station-wide — a completely separate path from `POST /api/fonts/{slug}/install`
 * (`FontInstallModal`), the one explicit, confirmed action that actually persists a pack.
 *
 * <b>Transient cleanup.</b> The effect's own cleanup removes the loaded `FontFace` from
 * `document.fonts` and revokes the object URL — the SAME two handles this effect created, and
 * NOTHING else. A stale in-flight fetch is fenced by the same `cancelled` flag
 * `ThemeDetailPreview` already uses, so a fast slug-to-slug re-select (or an unmount mid-fetch) can
 * never apply a response, add a face, or leak an object URL for a specimen the operator has
 * already moved past.
 */
export function SpecimenBlock({ slug, specimenFile }: SpecimenBlockProps): ReactNode {
  const [state, setState] = useState<SpecimenState>({ kind: "loading" });

  useEffect(() => {
    if (specimenFile === null) {
      setState({ kind: "error", message: "This pack has no readable specimen face." });
      return;
    }

    let cancelled = false;
    let loadedFace: FontFace | null = null;
    let objectUrl: string | null = null;
    setState({ kind: "loading" });

    const localFamily = `specimen-${slug}`;
    const assetUrl = `/api/catalog/entries/${encodeURIComponent(slug)}/assets/${encodeURIComponent(specimenFile)}`;

    (async () => {
      try {
        const resp = await fetch(assetUrl);
        if (cancelled) return;

        if (!resp.ok) {
          const message = await readErrorMessage(resp);
          if (cancelled) return;
          setState({ kind: "error", message });
          return;
        }

        const blob = await resp.blob();
        if (cancelled) return;

        const url = URL.createObjectURL(blob);
        const face = new FontFace(localFamily, `url(${url})`);

        let loaded: FontFace;
        try {
          loaded = await face.load();
        } catch {
          URL.revokeObjectURL(url);
          if (!cancelled) setState({ kind: "error", message: "This pack's face failed to load." });
          return;
        }
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }

        document.fonts.add(loaded);
        loadedFace = loaded;
        objectUrl = url;
        setState({ kind: "loaded", localFamily });
      } catch {
        if (!cancelled) setState({ kind: "error", message: "Network error — check your connection" });
      }
    })();

    // No retry, ever (SPEC F104.4/AC3's "no crash, no partial install" posture applied to a read):
    // one fetch attempt per (slug, specimenFile) pair, cleaned up below the instant either changes
    // or this component unmounts — never a polling loop or an automatic re-attempt on failure.
    return () => {
      cancelled = true;
      if (loadedFace !== null) document.fonts.delete(loadedFace);
      if (objectUrl !== null) URL.revokeObjectURL(objectUrl);
    };
  }, [slug, specimenFile]);

  if (state.kind === "loading") {
    return (
      <div className="space-y-2">
        <Skeleton className="h-20 w-full" />
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
    <div data-testid="font-specimen" className="rounded-[6px] border border-line bg-bg p-4">
      {/* The pack's OWN face, rendered via the LOCAL `specimen-<slug>` family name this effect
          generated above — never the manifest's own `family` string interpolated into CSS here
          (the T199/T200 stored-family obligation, re-stated by SPEC F104's own STORED
          FAMILY/STYLE remarks: `family` is free-form prose this station has never bounded to a
          safe CSS-identifier shape, unlike `slug`, which is already validated). A deliberate,
          narrow exception to "semantic tokens only" (design-aesthetic) and to "Fraunces is
          display-only, body text is never serif" — this text IS the specimen being judged, not
          app chrome, mirroring `ThemeSwatchChips`' own documented exception for raw theme hex. */}
      <p style={{ fontFamily: state.localFamily }} className="text-[1.7rem] leading-snug text-ink">
        The quick brown fox jumps over the lazy dog
      </p>
      <p style={{ fontFamily: state.localFamily }} className="mt-2 text-[1.2rem] leading-snug text-ink">
        AaBbCcDdEe 0123456789
      </p>
      <p className="mt-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
        Admin-only specimen — not installed
      </p>
    </div>
  );
}
