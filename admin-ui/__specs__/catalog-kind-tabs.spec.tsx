// @jest-environment jsdom
// gh-#372 — The Community Catalog shelf silos kinds by tab: Personas | Themes | Fonts | Shows,
// URL-driven via `?kind=` (plural in the URL, the wire kind singular as the tab id), the shared
// components/ui/tab-strip.tsx markup (gh-#393's extraction). Per-kind FILTERING of the grid itself
// is pinned where each kind's own suite already renders the client (theme-catalog-shelf.spec.tsx's
// "each kind holds its own tab" scenario, and every kind suite's `activeKind` prop) — this file
// covers the strip, the `?kind=` resolver, and the per-kind empty state.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { PersonaCatalogTabs, resolveCatalogKind } from "../app/(authed)/persona-catalog/PersonaCatalogTabs";
import type { CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";
import type { PersonaCatalogClient as PersonaCatalogClientType } from "../app/(authed)/persona-catalog/PersonaCatalogClient";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let PersonaCatalogClient: typeof PersonaCatalogClientType;

beforeEach(async () => {
  mockedUseRouter.mockReturnValue({ push: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
});

const PERSONA_ENTRY: CatalogShelfEntryDto = {
  slug: "late-night-lena",
  kind: "persona",
  audience: "everyone",
  bestFor: ["late-night"],
  preview: null,
  fontByteTotal: null,
  fontFamily: null,
};

describe("Feature: the shelf silos kinds by tab (gh-#372)", () => {
  describe("Scenario: ?kind= resolves to a shelf kind", () => {
    it("maps the plural URL values to the wire kinds", () => {
      expect(["themes", "fonts", "shows"].map(resolveCatalogKind)).toEqual(["theme", "font", "show"]);
    });

    it("defaults to persona when absent or unrecognised", () => {
      expect(resolveCatalogKind(undefined)).toBe("persona");
      expect(resolveCatalogKind("hats")).toBe("persona");
      expect(resolveCatalogKind(["themes", "fonts"])).toBe("persona");
    });
  });

  describe("Scenario: the tab strip lists every kind, always", () => {
    it("renders all four tabs with their ?kind= hrefs", () => {
      render(<PersonaCatalogTabs activeKind="persona" />);

      const nav = screen.getByRole("navigation", { name: "Catalog kinds" });
      expect(within(nav).getByRole("link", { name: "Personas" })).toHaveAttribute("href", "/persona-catalog");
      expect(within(nav).getByRole("link", { name: "Themes" })).toHaveAttribute("href", "/persona-catalog?kind=themes");
      expect(within(nav).getByRole("link", { name: "Fonts" })).toHaveAttribute("href", "/persona-catalog?kind=fonts");
      expect(within(nav).getByRole("link", { name: "Shows" })).toHaveAttribute("href", "/persona-catalog?kind=shows");
    });

    it("marks only the active tab with aria-current", () => {
      render(<PersonaCatalogTabs activeKind="show" />);

      expect(screen.getByRole("link", { name: "Shows" })).toHaveAttribute("aria-current", "page");
      expect(screen.getByRole("link", { name: "Personas" })).not.toHaveAttribute("aria-current");
    });
  });

  describe("Scenario: a kind tab is empty while the shelf is not", () => {
    it("names the kind in its own empty state instead of rendering the grid", () => {
      render(
        <PersonaCatalogClient
          activeKind="show"
          initialIndex={{ entries: [PERSONA_ENTRY], fetchedAt: "2026-08-12T00:00:00Z", unreachable: false }}
        />
      );

      expect(screen.getByText("No shows on the shelf")).toBeInTheDocument();
      expect(screen.queryByRole("list", { name: "Community catalog entries" })).not.toBeInTheDocument();
    });

    it("keeps the whole-shelf empty state when the index itself is empty", () => {
      render(
        <PersonaCatalogClient
          activeKind="persona"
          initialIndex={{ entries: [], fetchedAt: "2026-08-12T00:00:00Z", unreachable: false }}
        />
      );

      expect(screen.getByText("Nothing on the shelf yet")).toBeInTheDocument();
    });
  });
});
