"use client";

import { createContext, useContext, useMemo, type ReactNode } from "react";
import { parseIconPackDefinition, type IconPackDefinition } from "@/lib/icon-pack";

const IconPackDefinitionContext = createContext<IconPackDefinition | null>(null);

export interface IconPackProviderProps {
  /**
   * The active pack's raw canonical JSON text, straight off the authed layout's own server-side
   * `GET /api/icon-packs/active` read (SPEC F130.4, PLAN T304 rider 6) — folded into the SAME
   * per-navigation fetch the layout already performs for the theme switcher's own settings
   * snapshot (`app/(authed)/layout.tsx`'s own `fetchSettingsSnapshot` idiom), never a per-page
   * CLIENT fetch of an uncached route. `null` for every "house icons" shape: `Station:IconPack`
   * unset, a dangling slug (SPEC F130.5's fail-open uninstall), or the read itself failing.
   */
  definitionText: string | null;
  children: ReactNode;
}

/**
 * Provides the station's active icon pack — already parsed defensively, once — to every `Icon`
 * call site below it (SPEC F130.3/F130.4, STORY-337, PLAN T304). Mounted ONCE at the authed shell
 * (`app/(authed)/layout.tsx`), never per-page: `useMemo` re-parses only when `definitionText`
 * itself changes (a fresh server render — a navigation, or a settings/install change reflected on
 * the next one), not on every re-render this provider's own subtree causes.
 *
 * The context's OWN default value is `null` (no ancestor provider) — deliberate: every existing
 * isolated component render (a jest test mounting e.g. `<Sidebar />` or `<RatingControls />` with
 * no shell above it) keeps rendering house icons exactly as before this task, with ZERO test
 * changes needed for the swap itself — `null` here IS "house icons", the identical fail-open shape
 * an uninstalled active pack already resolves to server-side (SPEC F130.5).
 */
export function IconPackProvider({ definitionText, children }: IconPackProviderProps): ReactNode {
  const definition = useMemo(
    () => (definitionText === null ? null : parseIconPackDefinition(definitionText)),
    [definitionText]
  );

  return <IconPackDefinitionContext.Provider value={definition}>{children}</IconPackDefinitionContext.Provider>;
}

/** The active pack's already-parsed definition, or `null` for house icons — `Icon.tsx`'s own
 * resolver is the one intended reader; nothing else in the app should need this directly. */
export function useActiveIconPackDefinition(): IconPackDefinition | null {
  return useContext(IconPackDefinitionContext);
}
