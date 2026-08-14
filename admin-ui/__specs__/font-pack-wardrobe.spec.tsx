// @jest-environment jsdom
// STORY-284 — The wardrobe is inspectable (SPEC F104.7 · PLAN T203); AC2 is the 🖐️ T204 gate.
// Nav label/route renamed "Library" → "Wardrobe" at PLAN T204 (Dean's ruling) — this file was
// font-pack-library.spec.tsx before that rename; no behavior changed, only names.
//
// Runner: Jest. WardrobeClient renders GET /api/fonts's own listing — family (title), faces
// (style + byte size via the shared font-format.ts helper), the licence/version/subset line, and
// the "Installed · <slug> · <date>" db/25 provenance chip (AC1). `timeZone="UTC"` is pinned
// explicitly (the StatusTiles/BoothLogFeed/PersonasClient/SettingsForm house idiom, T105/T187) so
// the date half of the provenance chip is deterministic regardless of the host's TZ.
//
// gh-#428: every non-empty render now also renders `UninstallPackButton` per card, which calls
// `useConfirm()`/`useRouter()` unconditionally — so every such render here needs `ConfirmDialogProvider`
// and a mocked `next/navigation`, mirroring `wardrobe-uninstall-pack.spec.tsx`'s own harness. This
// file's own Facts never click Uninstall (that's the dedicated spec file's concern) — the wrapper is
// here only so these read-only listing Facts keep rendering at all.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import type { FontLibraryPackDto } from "../app/(authed)/wardrobe/types";
import type { WardrobeClient as WardrobeClientType } from "../app/(authed)/wardrobe/WardrobeClient";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let WardrobeClient: typeof WardrobeClientType;

beforeEach(async () => {
  mockedUseRouter.mockReturnValue({ refresh: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  // Dynamic import after the mock is in place — the directory's established convention
  // (catalog-purge-unavailable.spec.tsx's renderAction does the same); WardrobeClient renders
  // UninstallPackButton, which calls useRouter() unconditionally.
  ({ WardrobeClient } = await import("../app/(authed)/wardrobe/WardrobeClient"));
});

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

describe("Feature: the wardrobe is inspectable", () => {
  describe("Scenario: the wardrobe lists installed packs", () => {
    it("shows family, faces, byte sizes, and licence per pack (T203, AC1)", () => {
      render(
        <ConfirmDialogProvider>
          <WardrobeClient packs={[SPACE_GROTESK_PACK]} timeZone="UTC" />
        </ConfirmDialogProvider>
      );

      const list = screen.getByRole("list", { name: "Installed font packs" });
      const card = within(list).getByText("Space Grotesk").closest("li");
      if (card === null) throw new Error("No <li> ancestor for the pack card");

      expect(within(card).getByText("Space Grotesk")).toBeInTheDocument(); // family, as the title
      expect(within(card).getByText("normal — 8 KiB")).toBeInTheDocument(); // style + formatFontByteTotal
      expect(within(card).getByText("OFL-1.1 · v1.0 · latin")).toBeInTheDocument(); // licence line
    });

    it("shows 'Installed · <slug> · <date>' provenance per pack (T203, AC1)", () => {
      render(
        <ConfirmDialogProvider>
          <WardrobeClient packs={[SPACE_GROTESK_PACK]} timeZone="UTC" />
        </ConfirmDialogProvider>
      );

      expect(screen.getByText("Installed · space-grotesk · Aug 5, 2026")).toBeInTheDocument();
    });

    // gh-#428 — the wardrobe stops being pure-read-only: each card gets its own uninstall
    // affordance. The button's own confirm/DELETE/toast/refresh cycle is UninstallPackButton's own
    // concern (wardrobe-uninstall-pack.spec.tsx); this Fact only proves WardrobeClient wires one in
    // per card, named for that card's own pack.
    it("renders an Uninstall button per pack, named for that pack", () => {
      render(
        <ConfirmDialogProvider>
          <WardrobeClient packs={[SPACE_GROTESK_PACK]} timeZone="UTC" />
        </ConfirmDialogProvider>
      );

      expect(screen.getByRole("button", { name: "Uninstall Space Grotesk" })).toBeInTheDocument();
    });
  });

  describe("Scenario: the wardrobe is empty", () => {
    // T203 review finding F3: the empty-state CTA must not point at /persona-catalog when the
    // catalog is disabled — that route itself 404s off-catalog, the exact dead end the Wardrobe nav
    // item's own deliberate ungating (SPEC F104.8) exists to let an operator avoid.
    it("names the reason and offers the Community Catalog CTA when the catalog is enabled", () => {
      render(<WardrobeClient packs={[]} timeZone="UTC" catalogEnabled />);

      expect(screen.getByText("No packs installed")).toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Browse the Community Catalog" })).toHaveAttribute(
        "href",
        "/persona-catalog"
      );
    });

    it("points at Settings instead of the catalog when the catalog is disabled", () => {
      render(<WardrobeClient packs={[]} timeZone="UTC" catalogEnabled={false} />);

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
      render(
        <ConfirmDialogProvider>
          <WardrobeClient packs={[packWithoutManifest]} timeZone="UTC" />
        </ConfirmDialogProvider>
      );

      expect(screen.getByText("Licence unknown")).toBeInTheDocument();
    });
  });
});
