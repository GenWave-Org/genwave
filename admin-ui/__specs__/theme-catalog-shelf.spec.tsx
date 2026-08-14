// @jest-environment jsdom
// STORY-273 — The shelf lists themes beside personas (SPEC F103.3, F103.4)
//
// Runner: Jest. The community-catalog shelf gains a second kind: theme entries are listed on the
// SAME shelf as personas, routed by `kind`, and previewed cheaply — a theme card renders colour
// chips from the entry's `preview` swatches with NO manifest fetch and NO CSS composition, so a
// wild card-to-card browse costs nothing beyond the one index read.
//
// RTL drives PersonaCatalogClient directly with a fake initialIndex (mirrors
// persona-catalog-page.spec.tsx's own "Feature: Browsing the shelf" block). `next/navigation` is
// mocked (PersonaCatalogClient calls useRouter() unconditionally since PLAN T103) and the
// component is dynamically imported AFTER that mock registers — see
// persona-catalog-page.spec.tsx's own remarks on why a static top-level import would bind the
// REAL next/navigation export first under this project's SWC-based jest transform. Landed at
// T185 — un-pinned from the it.todo skeleton this file used to carry.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
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
// Fixtures — the theme entry's preview swatches are golden-frequency's own light/dark tokens
// (tests/GenWave.Host.Tests/Fixtures/golden.theme.json, the same values the app-side
// mixed-catalog-index.json fixture uses), so both stacks pin the same realistic colours rather
// than two unrelated palettes that happen to both be five hex strings.
// ---------------------------------------------------------------------------

const PERSONA_ENTRY: CatalogShelfEntryDto = {
  slug: "late-night-lena",
  kind: "persona",
  audience: "everyone",
  bestFor: ["late-night"],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const THEME_ENTRY: CatalogShelfEntryDto = {
  slug: "golden-frequency",
  kind: "theme",
  audience: "everyone",
  bestFor: [],
  preview: {
    light: { bg: "#f7ecd2", surface: "#fff8e6", ink: "#2c2410", accent: "#b8860b", "accent-2": "#4f6b52" },
    dark: { bg: "#171205", surface: "#241c09", ink: "#f4ecce", accent: "#e0a52c", "accent-2": "#7fa382" },
  },
  fontFamily: null,
  fontByteTotal: null,
};

const THEME_ENTRY_WITHOUT_PREVIEW: CatalogShelfEntryDto = {
  slug: "gilded-static",
  kind: "theme",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

// F3 review finding: `preview` is typed `CatalogThemePreview | null`, but that type only holds
// while the api keeps serializing with `DefaultIgnoreCondition = Never` — if it ever started
// OMITTING null properties instead, this field would arrive as `undefined`, not `null`. Built via
// an explicit cast (not the type above) because the omission itself is the point: a real wire
// payload missing the key entirely, which the declared type can't express.
const THEME_ENTRY_WITH_UNDEFINED_PREVIEW = {
  slug: "gilded-static",
  kind: "theme",
  audience: "everyone",
  bestFor: [],
} as unknown as CatalogShelfEntryDto;

describe("Feature: the catalog shelf lists themes beside personas", () => {
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

  describe("Scenario: each kind holds its own tab", () => {
    // Was "both kinds appear on one shelf" (T185, AC1) — gh-#372 replaces the one mixed grid with
    // a tab per kind, so the routed-by-kind guarantee is now asserted per tab: the active kind's
    // entries render (through their own card component), every other kind's stay off the grid.
    it("the themes tab lists the theme entry and not the persona entry (gh-#372)", () => {
      render(
        <PersonaCatalogClient activeKind="theme"
          initialIndex={{ entries: [PERSONA_ENTRY, THEME_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Golden Frequency")).toBeInTheDocument();
      expect(within(grid).queryByRole("button", { name: /Late Night Lena/ })).toBeNull();
    });

    it("the personas tab lists the persona entry and not the theme entry (gh-#372)", () => {
      render(
        <PersonaCatalogClient activeKind="persona"
          initialIndex={{ entries: [PERSONA_ENTRY, THEME_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      // The persona entry keeps its existing interactive card (a <button>, click-through detail).
      expect(within(grid).getByRole("button", { name: /Late Night Lena/ })).toBeInTheDocument();
      expect(within(grid).queryByText("Golden Frequency")).toBeNull();
    });
  });

  describe("Scenario: a theme card previews cheaply from meta", () => {
    it("renders colour swatch chips from the entry's meta preview swatches (T185, AC2)", () => {
      render(<PersonaCatalogClient activeKind="theme" initialIndex={{ entries: [THEME_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }} />);

      // `data-testid`, not a role/name query (review finding, N2): the chip row is `aria-hidden`
      // (decorative — the theme name carries the semantics), so it — and its `<li>`s — are outside
      // the accessibility tree `getByRole` normally sees; `{ hidden: true }` opts the nested query
      // back in to reach the swatches themselves.
      const chips = screen.getByTestId("theme-preview-swatches");
      const swatches = within(chips)
        .getAllByRole("listitem", { hidden: true })
        .map((li) => li.firstElementChild as HTMLElement);

      expect(swatches).toHaveLength(5);
      expect(swatches[0]).toHaveStyle({ backgroundColor: "#f7ecd2" }); // bg
      expect(swatches[4]).toHaveStyle({ backgroundColor: "#4f6b52" }); // accent-2
    });

    it("renders no swatch chips when the entry carries no preview, not an error", () => {
      render(
        <PersonaCatalogClient activeKind="theme"
          initialIndex={{ entries: [THEME_ENTRY_WITHOUT_PREVIEW], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      expect(screen.getByText("Gilded Static")).toBeInTheDocument();
      expect(screen.queryByTestId("theme-preview-swatches")).not.toBeInTheDocument();
    });

    it("renders no swatch chips, and does not crash, when preview is undefined rather than null (F3)", () => {
      render(
        <PersonaCatalogClient activeKind="theme"
          initialIndex={{ entries: [THEME_ENTRY_WITH_UNDEFINED_PREVIEW], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      expect(screen.getByText("Gilded Static")).toBeInTheDocument();
      expect(screen.queryByTestId("theme-preview-swatches")).not.toBeInTheDocument();
    });

    it("fetches no theme manifest and composes no CSS while rendering shelf cards (T185, AC3)", () => {
      const fetchMock = jest.fn<typeof fetch>();
      global.fetch = fetchMock as unknown as typeof fetch;

      render(
        <PersonaCatalogClient activeKind="theme"
          initialIndex={{ entries: [PERSONA_ENTRY, THEME_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      // Rendering the shelf alone — no click, no interaction — must never touch the network: the
      // whole card (name, badge, swatch chips) is painted straight off the already-fetched index
      // prop, never a per-card manifest fetch.
      expect(fetchMock).not.toHaveBeenCalled();
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: the shelf survives the catalog being disabled", () => {
    it("shows the not-available state, not an error, when Community:CatalogIndexUrl is empty (T185, AC4)", () => {
      render(<PersonaCatalogClient activeKind="theme" initialIndex={{ entries: null, fetchedAt: null, unreachable: true }} />);

      expect(screen.getByText("Catalog unreachable")).toBeInTheDocument();
    });
  });

  describe("Scenario: an entry with an unrecognised kind renders nothing (N4 review finding)", () => {
    it("renders no card at all for a kind that is neither theme nor persona", () => {
      // The server already drops any kind it doesn't recognise (CatalogIndexValidator, F103.1/AC6)
      // — this pins the client's OWN defence, should that server invariant ever slip: routing by an
      // exhaustive `switch`, not a ternary whose `else` branch would mis-render this AS a persona.
      const unknownKindEntry = {
        slug: "mystery-entry",
        kind: "mystery",
        audience: "everyone",
        bestFor: [],
        preview: null,
      } as unknown as CatalogShelfEntryDto;

      render(
        <PersonaCatalogClient activeKind="theme"
          initialIndex={{ entries: [unknownKindEntry], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      expect(screen.queryByText("Mystery Entry")).not.toBeInTheDocument();
    });
  });
});
