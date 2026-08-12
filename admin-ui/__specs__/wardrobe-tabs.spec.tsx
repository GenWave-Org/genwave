// @jest-environment jsdom
// gh-#393 — The Wardrobe silos the catalog kinds by tab (the gh-#372 shelf treatment applied to
// the installed side): Personas | Themes | Fonts | Shows, URL-driven via `?tab=` (the CatalogTabs/
// BoothLogTabs idiom, now the shared components/ui/tab-strip.tsx), every tab present even when
// empty (Dean's ruling on the issue — an empty kind shows its own empty state, never a hidden tab).
//
// Runner: Jest. Three surfaces: resolveWardrobeTab (the `?tab=` → tab mapping), WardrobeTabs (the
// strip itself), and InstalledEntriesList (the read-only per-kind listing the Personas/Themes/Shows
// tabs share — fonts keep their own WardrobeClient, covered by font-pack-wardrobe.spec.tsx
// unchanged). `timeZone="UTC"` pins the provenance chip's date half, the house idiom (T105/T187).

import { describe, it, expect } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { InstalledEntriesList } from "../app/(authed)/wardrobe/InstalledEntriesList";
import { resolveWardrobeTab, WardrobeTabs } from "../app/(authed)/wardrobe/WardrobeTabs";
import type { InstalledEntryRow } from "../app/(authed)/wardrobe/types";

const HIRED_PERSONA: InstalledEntryRow = {
  slug: "big-tony-marconi",
  name: "Big Tony Marconi",
  detail: null,
  importedFrom: "big-tony-marconi",
  importedAt: "2026-08-05T12:00:00Z",
};

const IMPORTED_SHOW: InstalledEntryRow = {
  slug: "til-sunrise",
  name: "'Til Sunrise",
  detail: "Overnights, unhurried.",
  importedFrom: "til-sunrise",
  importedAt: "2026-08-11T09:00:00Z",
};

describe("Feature: the Wardrobe silos kinds by tab (gh-#393)", () => {
  describe("Scenario: ?tab= resolves to a wardrobe tab", () => {
    it("defaults to personas when absent", () => {
      expect(resolveWardrobeTab(undefined)).toBe("personas");
    });

    it("passes each named tab through", () => {
      expect(["themes", "fonts", "shows"].map(resolveWardrobeTab)).toEqual(["themes", "fonts", "shows"]);
    });

    it("falls back to personas on anything unrecognised", () => {
      expect(resolveWardrobeTab("hats")).toBe("personas");
      expect(resolveWardrobeTab(["fonts", "shows"])).toBe("personas");
    });
  });

  describe("Scenario: the tab strip lists every kind, always", () => {
    it("renders all four tabs with their ?tab= hrefs", () => {
      render(<WardrobeTabs activeTab="personas" />);

      const nav = screen.getByRole("navigation", { name: "Wardrobe sections" });
      expect(within(nav).getByRole("link", { name: "Personas" })).toHaveAttribute("href", "/wardrobe");
      expect(within(nav).getByRole("link", { name: "Themes" })).toHaveAttribute("href", "/wardrobe?tab=themes");
      expect(within(nav).getByRole("link", { name: "Fonts" })).toHaveAttribute("href", "/wardrobe?tab=fonts");
      expect(within(nav).getByRole("link", { name: "Shows" })).toHaveAttribute("href", "/wardrobe?tab=shows");
    });

    it("marks only the active tab with aria-current", () => {
      render(<WardrobeTabs activeTab="fonts" />);

      expect(screen.getByRole("link", { name: "Fonts" })).toHaveAttribute("aria-current", "page");
      expect(screen.getByRole("link", { name: "Personas" })).not.toHaveAttribute("aria-current");
    });
  });

  describe("Scenario: a kind tab lists its installed entries", () => {
    it("renders a card per row with the kind's own provenance verb", () => {
      render(
        <InstalledEntriesList
          rows={[HIRED_PERSONA]}
          ariaLabel="Hired personas"
          provenanceVerb="Hired"
          emptyTitle="No personas hired"
          emptyReason="unused here"
          timeZone="UTC"
        />
      );

      const list = screen.getByRole("list", { name: "Hired personas" });
      expect(within(list).getByText("Big Tony Marconi")).toBeInTheDocument();
      expect(within(list).getByText("Hired · big-tony-marconi · Aug 5, 2026")).toBeInTheDocument();
    });

    it("renders the secondary line only when a row carries one", () => {
      render(
        <InstalledEntriesList
          rows={[IMPORTED_SHOW]}
          ariaLabel="Imported shows"
          provenanceVerb="Imported"
          emptyTitle="No shows imported"
          emptyReason="unused here"
          timeZone="UTC"
        />
      );

      const list = screen.getByRole("list", { name: "Imported shows" });
      expect(within(list).getByText("Overnights, unhurried.")).toBeInTheDocument();
      expect(within(list).getByText("Imported · til-sunrise · Aug 11, 2026")).toBeInTheDocument();
    });
  });

  describe("Scenario: a kind tab is empty", () => {
    // The T203 review finding F3 CTA swap, inherited from WardrobeClient's own empty state: a
    // disabled catalog must never leave an empty tab pointing at /persona-catalog (it 404s
    // off-catalog).
    it("names the reason and offers the catalog CTA when the catalog is enabled", () => {
      render(
        <InstalledEntriesList
          rows={[]}
          ariaLabel="Hired personas"
          provenanceVerb="Hired"
          emptyTitle="No personas hired"
          emptyReason="Browse the Community Catalog to hire a DJ for this station."
          catalogEnabled
          timeZone="UTC"
        />
      );

      expect(screen.getByText("No personas hired")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Browse the Community Catalog" })).toHaveAttribute(
        "href",
        "/persona-catalog"
      );
    });

    it("points at Settings instead when the catalog is disabled", () => {
      render(
        <InstalledEntriesList
          rows={[]}
          ariaLabel="Hired personas"
          provenanceVerb="Hired"
          emptyTitle="No personas hired"
          emptyReason="unused when disabled"
          catalogEnabled={false}
          timeZone="UTC"
        />
      );

      expect(screen.getByText("No personas hired")).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Browse the Community Catalog" })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Open Settings" })).toHaveAttribute("href", "/settings");
    });
  });
});
