// @jest-environment jsdom
// STORY-281 — Packs on the shelf + the honest specimen (SPEC F104.3, F104.4 · PLAN T201/T202)
//
// Runner: Jest. T201 lands the shelf half: a font pack renders beside theme/persona entries on the
// SAME shelf, routed by `kind`, previewed from the index row's own `fontFamily`/`fontByteTotal`
// (T194) — no manifest fetch, no asset fetch, ever, while browsing. Mirrors
// theme-catalog-shelf.spec.tsx's own "renders from meta alone, fetches nothing while browsing"
// idiom for theme cards (T185). `description` is deliberately NOT asserted here (STORY-281 AC1
// reconciliation, T201): the shelf wire (`CatalogShelfEntryDto`) carries only
// `fontFamily`/`fontByteTotal` — `description` rides the per-entry detail fetch T202 builds. T202's
// own specimen/close/degrade scenarios stay `it.todo` below; this task doesn't touch them.
//
// Review findings F1/F2 (T201 follow-up): the card's title now reads `fontFamily ??
// prettifySlug(slug)` with no separate, often-duplicated family line (F1), and the byte line's guard
// is falsy-tolerant (`!= null`, F2) so an omitted wire field degrades instead of rendering the
// literal word "undefined". The three specs below pin those: the family/slug COLLISION shape (one
// occurrence, not two), the `fontFamily: null` fallback, and an omitted-field payload.

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { useRouter } from "next/navigation";
import type { PersonaCatalogClient as PersonaCatalogClientComponent } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import type { CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let PersonaCatalogClient: typeof PersonaCatalogClientComponent;

beforeAll(async () => {
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
});

// ---------------------------------------------------------------------------
// Fixture — the byte total (7844) is the SAME value
// tests/GenWave.Host.Tests/Fixtures/golden.font.json's "Space Grotesk" pack carries (T193/T194's
// golden parity precedent, theme-catalog-shelf.spec.tsx's own golden.theme.json precedent). The
// slug deliberately does NOT title-case to the same string as the family ("Libre Grotesk" vs "Space
// Grotesk") — a real pack's slug and its font's family name are two independently authored strings
// (FONTS.md), and keeping them distinct here lets each assertion below target one specific field
// without an ambiguous duplicate-text match.
// ---------------------------------------------------------------------------

const FONT_ENTRY: CatalogShelfEntryDto = {
  slug: "libre-grotesk",
  kind: "font",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: "Space Grotesk",
  fontByteTotal: 7844,
};

// Review finding F1 — a REAL pack's slug and family collide: the authoring convention is slug =
// kebab-cased family, so "space-grotesk" title-cases to the exact same string ("Space Grotesk") the
// `fontFamily` field carries. The card's title reads `fontFamily ?? prettifySlug(slug)` with no
// separate family line under it (F1's "cleaner option") — the collision spec below pins that the
// text renders exactly ONCE, not twice.
const COLLISION_FONT_ENTRY: CatalogShelfEntryDto = {
  slug: "space-grotesk",
  kind: "font",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: "Space Grotesk",
  fontByteTotal: 7844,
};

// `fontFamily: null` — an older index, or a malformed value `CatalogIndexValidator` couldn't admit
// (T194). The title falls back to the slug-derived title instead of rendering blank.
const FONT_ENTRY_WITHOUT_FAMILY: CatalogShelfEntryDto = {
  slug: "libre-grotesk",
  kind: "font",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: 7844,
};

// Review finding F2 — `fontFamily`/`fontByteTotal` are typed `string | null`/`number | null`, but
// that type only holds while the api keeps serializing with `DefaultIgnoreCondition = Never` — if it
// ever started OMITTING null properties instead, these fields would arrive as `undefined`, not
// `null`. Built via an explicit cast (not the type above) because the omission itself is the point: a
// real wire payload missing the keys entirely, which the declared type can't express. Mirrors
// theme-catalog-shelf.spec.tsx's own `THEME_ENTRY_WITH_UNDEFINED_PREVIEW` fixture (its F3 review
// finding, the same falsy-tolerant contract this file's F2 finding applies to `fontFamily`/
// `fontByteTotal`).
const FONT_ENTRY_WITH_UNDEFINED_FIELDS = {
  slug: "libre-grotesk",
  kind: "font",
  audience: "everyone",
  bestFor: [],
  preview: null,
} as unknown as CatalogShelfEntryDto;

describe("Feature: packs on the shelf with an honest specimen", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ push: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: the shelf card is meta-only", () => {
    it("renders family and byte total from the shelf payload alone (T201, AC1)", () => {
      render(
        <PersonaCatalogClient
          initialIndex={{ entries: [FONT_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      // The title itself IS the family (F1 review finding) — `fontFamily ?? prettifySlug(slug)` —
      // not a slug-derived title plus a separate, duplicated family line underneath it.
      expect(within(grid).getByText("Space Grotesk")).toBeInTheDocument(); // fontFamily, as the title
      expect(within(grid).getByText("8 KiB")).toBeInTheDocument(); // fontByteTotal, human-readable
    });

    it("issues no asset fetch on browse (T201, AC1)", () => {
      const fetchMock = jest.fn<typeof fetch>();
      global.fetch = fetchMock as unknown as typeof fetch;

      render(
        <PersonaCatalogClient
          initialIndex={{ entries: [FONT_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      // Rendering the shelf alone — no click, no interaction — must never touch the network: the
      // whole card (name, badge, family, byte total) is painted straight off the already-fetched
      // index prop, never a per-card manifest or asset fetch.
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it("renders the family-collision title exactly once, not a duplicated family line (F1 review finding)", () => {
      render(
        <PersonaCatalogClient
          initialIndex={{ entries: [COLLISION_FONT_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      // slug "space-grotesk" title-cases to the SAME text the family carries ("Space Grotesk") — the
      // title is now the ONLY place that text renders; a separate family line under it would have
      // printed the identical string twice on every real pack.
      expect(within(grid).getAllByText("Space Grotesk")).toHaveLength(1);
    });

    it("falls back to the slug-derived title when fontFamily is null (F1 review finding)", () => {
      render(
        <PersonaCatalogClient
          initialIndex={{ entries: [FONT_ENTRY_WITHOUT_FAMILY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Libre Grotesk")).toBeInTheDocument();
    });

    it("degrades sanely, with no literal \"undefined\" text, when fontFamily/fontByteTotal are omitted from the wire rather than null (F2 review finding)", () => {
      render(
        <PersonaCatalogClient
          initialIndex={{ entries: [FONT_ENTRY_WITH_UNDEFINED_FIELDS], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Libre Grotesk")).toBeInTheDocument(); // title falls back to the slug
      expect(within(grid).queryByText(/undefined/i)).not.toBeInTheDocument();
    });
  });
  describe("Scenario: the specimen is the real face", () => {
    it.todo("renders the specimen in the pack's hash-verified face (T202, AC2)");
    it.todo("discards everything on close — nothing installed, nothing station-wide (T202, AC2)");
  });
  describe("Scenario: an unreachable asset degrades", () => {
    it.todo("shows degraded copy without crashing on an integrity/connectivity failure (T202, AC3)");
  });
});
