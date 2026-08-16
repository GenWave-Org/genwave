import type { ReactNode } from "react";
import { TabStrip, type TabStripTab } from "@/components/ui/tab-strip";
import type { CatalogEntryKind } from "./types";

interface PersonaCatalogTabsProps {
  activeKind: CatalogEntryKind;
}

/** Tab ids are the WIRE kinds (singular, the F103.1 discriminator each shelf row already carries);
 * the `?kind=` URL values stay plural for readability — `resolveCatalogKind` maps between them. */
const TABS: TabStripTab<CatalogEntryKind>[] = [
  { id: "persona", label: "Personas", href: "/persona-catalog" },
  { id: "theme", label: "Themes", href: "/persona-catalog?kind=themes" },
  { id: "font", label: "Fonts", href: "/persona-catalog?kind=fonts" },
  { id: "show", label: "Shows", href: "/persona-catalog?kind=shows" },
  { id: "avatar", label: "Avatars", href: "/persona-catalog?kind=avatars" },
  { id: "icon", label: "Icons", href: "/persona-catalog?kind=icons" },
];

/**
 * Resolves `?kind=` to a shelf kind (gh-#372, widened at PLAN T304) — the `resolveWardrobeTab`
 * posture: anything unrecognised (absent, an array, a stranger) falls back to personas, the
 * shelf's founding kind.
 */
export function resolveCatalogKind(raw: string | string[] | undefined): CatalogEntryKind {
  switch (raw) {
    case "themes":
      return "theme";
    case "fonts":
      return "font";
    case "shows":
      return "show";
    case "avatars":
      return "avatar";
    case "icons":
      return "icon";
    default:
      return "persona";
  }
}

/**
 * Personas | Themes | Fonts | Shows | Avatars | Icons tab strip for the Community Catalog shelf
 * (gh-#372, widened at PLAN T294/T304) — URL-driven via `?kind=`, the shared `TabStrip` markup
 * (gh-#393's extraction). One tab per kind: the flat mixed grid gave no way to tell a persona card
 * from a show card without opening it (the issue's own complaint — neither kind carries a badge),
 * and the pile got worse with every kind the shelf gained. Every tab renders even when its kind has
 * nothing on the shelf (the gh-#393 ruling applied here too) — the tab's own empty state names the
 * kind instead.
 */
export function PersonaCatalogTabs({ activeKind }: PersonaCatalogTabsProps): ReactNode {
  return <TabStrip tabs={TABS} activeTab={activeKind} ariaLabel="Catalog kinds" />;
}
