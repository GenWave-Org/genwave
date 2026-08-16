import type { ReactNode } from "react";
import { TabStrip, type TabStripTab } from "@/components/ui/tab-strip";

/** The catalog kinds an entry can be installed as (gh-#393, widened at PLAN T294), in the shelf's
 * own kind order. */
export type WardrobeTab = "personas" | "themes" | "fonts" | "shows" | "avatars";

interface WardrobeTabsProps {
  activeTab: WardrobeTab;
}

const TABS: TabStripTab<WardrobeTab>[] = [
  { id: "personas", label: "Personas", href: "/wardrobe" },
  { id: "themes", label: "Themes", href: "/wardrobe?tab=themes" },
  { id: "fonts", label: "Fonts", href: "/wardrobe?tab=fonts" },
  { id: "shows", label: "Shows", href: "/wardrobe?tab=shows" },
  { id: "avatars", label: "Avatars", href: "/wardrobe?tab=avatars" },
];

/**
 * Resolves `?tab=` to a wardrobe tab (gh-#393, widened at PLAN T294) — mirrors `catalog/page.tsx`'s
 * own `resolveTab` posture: anything unrecognised (absent, an array, a stranger) falls back to the
 * first tab rather than erroring. Every tab renders even when empty (Dean's ruling on gh-#393: an
 * empty kind shows its own empty state, never a hidden tab — unlike `settings-tabs.ts`'s
 * derive-from-data omission).
 */
export function resolveWardrobeTab(raw: string | string[] | undefined): WardrobeTab {
  return raw === "themes" || raw === "fonts" || raw === "shows" || raw === "avatars" ? raw : "personas";
}

/**
 * Personas | Themes | Fonts | Shows | Avatars tab strip for the Wardrobe (gh-#393, the gh-#372
 * shelf-tabs treatment applied to the installed side; widened at PLAN T294) — URL-driven via
 * `?tab=`, no client state, the shared `TabStrip` markup. One tab per catalog kind, siloing what was
 * becoming a mixed pile as kinds accumulated (the same complaint gh-#372 makes about the shelf
 * itself).
 */
export function WardrobeTabs({ activeTab }: WardrobeTabsProps): ReactNode {
  return <TabStrip tabs={TABS} activeTab={activeTab} ariaLabel="Wardrobe sections" />;
}
