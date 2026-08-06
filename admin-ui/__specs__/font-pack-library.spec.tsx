// @jest-environment jsdom
// STORY-284 — The library is inspectable (SPEC F104.7 · PLAN T203); AC2 is the 🖐️ T204 gate.
//
// Runner: Jest. FontLibraryClient renders GET /api/fonts's own listing — family (title), faces
// (style + byte size via the shared font-format.ts helper), the licence/version/subset line, and
// the "Installed · <slug> · <date>" db/25 provenance chip (AC1). `timeZone="UTC"` is pinned
// explicitly (the StatusTiles/BoothLogFeed/PersonasClient/SettingsForm house idiom, T105/T187) so
// the date half of the provenance chip is deterministic regardless of the host's TZ.

import { describe, it, expect } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { FontLibraryClient } from "../app/(authed)/library/FontLibraryClient";
import type { FontLibraryPackDto } from "../app/(authed)/library/types";

const SPACE_GROTESK_PACK: FontLibraryPackDto = {
  slug: "space-grotesk",
  family: "Space Grotesk",
  faces: [{ file: "space-grotesk-variable-latin.woff2", style: "normal", byteSize: 7844 }],
  license: "OFL-1.1",
  sourceUrl: "https://github.com/example/space-grotesk",
  version: "1.0",
  subset: "latin",
  importedFrom: "space-grotesk",
  importedAt: "2026-08-05T12:00:00Z",
};

describe("Feature: the library is inspectable", () => {
  describe("Scenario: the library lists installed packs", () => {
    it("shows family, faces, byte sizes, and licence per pack (T203, AC1)", () => {
      render(<FontLibraryClient packs={[SPACE_GROTESK_PACK]} timeZone="UTC" />);

      const list = screen.getByRole("list", { name: "Installed font packs" });
      const card = within(list).getByText("Space Grotesk").closest("li");
      if (card === null) throw new Error("No <li> ancestor for the pack card");

      expect(within(card).getByText("Space Grotesk")).toBeInTheDocument(); // family, as the title
      expect(within(card).getByText("normal — 8 KiB")).toBeInTheDocument(); // style + formatFontByteTotal
      expect(within(card).getByText("OFL-1.1 · v1.0 · latin")).toBeInTheDocument(); // licence line
    });

    it("shows 'Installed · <slug> · <date>' provenance per pack (T203, AC1)", () => {
      render(<FontLibraryClient packs={[SPACE_GROTESK_PACK]} timeZone="UTC" />);

      expect(screen.getByText("Installed · space-grotesk · Aug 5, 2026")).toBeInTheDocument();
    });
  });

  describe("Scenario: the library is empty", () => {
    // T203 review finding F3: the empty-state CTA must not point at /persona-catalog when the
    // catalog is disabled — that route itself 404s off-catalog, the exact dead end the Library nav
    // item's own deliberate ungating (SPEC F104.8) exists to let an operator avoid.
    it("names the reason and offers the Community Catalog CTA when the catalog is enabled", () => {
      render(<FontLibraryClient packs={[]} timeZone="UTC" catalogEnabled />);

      expect(screen.getByText("No packs installed")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Browse the Community Catalog" })).toHaveAttribute(
        "href",
        "/persona-catalog"
      );
    });

    it("points at Settings instead of the catalog when the catalog is disabled", () => {
      render(<FontLibraryClient packs={[]} timeZone="UTC" catalogEnabled={false} />);

      expect(screen.getByText("No packs installed")).toBeInTheDocument();
      expect(
        screen.getByText(
          "The Community Catalog is disabled — enable Community:CatalogIndexUrl in Settings to browse packs."
        )
      ).toBeInTheDocument();
      expect(screen.queryByRole("link", { name: "Browse the Community Catalog" })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Open Settings" })).toHaveAttribute("href", "/settings");
    });
  });

  describe("Scenario: a stored definition failed to re-parse", () => {
    it("degrades the licence line to 'Licence unknown' instead of rendering blank", () => {
      const packWithoutManifest: FontLibraryPackDto = {
        ...SPACE_GROTESK_PACK,
        license: null,
        version: null,
        subset: null,
      };
      render(<FontLibraryClient packs={[packWithoutManifest]} timeZone="UTC" />);

      expect(screen.getByText("Licence unknown")).toBeInTheDocument();
    });
  });
});
