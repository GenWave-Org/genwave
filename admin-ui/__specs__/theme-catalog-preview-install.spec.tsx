// @jest-environment jsdom
// STORY-274 — Previewing and installing a theme (SPEC F103.5, F103.6)
//
// Runner: Jest. Opening a theme's detail/review shows a LIVE composed mini-preview — the fetched
// manifest run through the same ThemeCssComposer into a SCOPED preview container (not :root), so a
// browser sees the real look before adopting it. Because v1 themes are colour-only over the
// already-loaded curated fonts, the preview loads NO new fonts (nothing to thrash on repeated
// opens). Confirming posts the manifest to POST /api/themes/{slug}/import; cancelling does nothing.
//
// Landed at T186 — un-pinned from the it.todo skeleton this file used to carry. RTL drives
// PersonaCatalogClient directly (mirrors theme-catalog-shelf.spec.tsx's own T185 harness):
// `next/navigation` is mocked and the component dynamically imported AFTER that mock registers —
// see persona-catalog-page.spec.tsx's own remarks on why a static top-level import would bind the
// REAL next/navigation export first under this project's SWC-based jest transform. The composer
// itself (ThemeCssComposer.ComposeScoped) is proven server-side in
// tests/GenWave.Host.Tests/Specs/Story274_ThemeCatalogPreview.cs — this file only pins the CLIENT
// wiring: which requests fire when, what reaches the container, and that cancel/error paths never
// crash or mutate anything.

jest.mock("next/navigation", () => ({
  ...jest.requireActual("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { useRouter } from "next/navigation";
import { Toaster } from "@/components/ui/toast";
import type { PersonaCatalogClient as PersonaCatalogClientComponent } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import { THEME_PREVIEW_CONTAINER_CLASS } from "../app/(authed)/persona-catalog/theme-preview";
import type { CatalogEntryDetailDto, CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let PersonaCatalogClient: typeof PersonaCatalogClientComponent;

beforeAll(async () => {
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
});

// ---------------------------------------------------------------------------
// Fixtures — golden-frequency's own manifest (mirrors tests/GenWave.Host.Tests/Fixtures/
// golden.theme.json and theme-catalog-shelf.spec.tsx's own light/dark tokens), so a scoped-compose
// regression here would use the same realistic shape the backend specs already pin.
// ---------------------------------------------------------------------------

const GOLDEN_MANIFEST_JSON = JSON.stringify({
  slug: "golden-frequency",
  name: "Golden Frequency",
  author: "GenWave",
  fonts: {
    display: {
      family: "Fraunces",
      assets: [{ src: "/fonts/fraunces-variable-latin.woff2", weight: "400 600", style: "normal" }],
    },
    sans: {
      family: "Source Sans 3",
      assets: [{ src: "/fonts/source-sans-3-variable-latin.woff2", weight: "400", style: "normal" }],
    },
  },
  modes: {
    light: { bg: "#f7ecd2", ink: "#2c2410" },
    dark: { bg: "#171205", ink: "#f4ecce" },
  },
});

const SCOPED_PREVIEW_CSS = [
  ".theme-live-preview {",
  "  --bg: #f7ecd2;",
  "  --ink: #2c2410;",
  '  --font-display: "Fraunces", Georgia, serif;',
  '  --font-sans: "Source Sans 3", system-ui, sans-serif;',
  "}",
  "",
].join("\n");

const THEME_ENTRY: CatalogShelfEntryDto = {
  slug: "golden-frequency",
  kind: "theme",
  audience: "everyone",
  bestFor: [],
  preview: null,
};

const THEME_DETAIL: CatalogEntryDetailDto = {
  card: GOLDEN_MANIFEST_JSON,
  meta: "{}",
  fetchedAt: "2026-08-05T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: [],
  author: null,
  description: null,
  samplePatter: [],
};

const ENTRY_URL = "/api/catalog/entries/golden-frequency";
const PREVIEW_URL = "/api/themes/preview";
const IMPORT_URL = "/api/themes/golden-frequency/import?catalogSlug=golden-frequency";

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    text: jest.fn<() => Promise<string>>().mockResolvedValue(JSON.stringify(body)),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

function makeCssResponse(status: number, css: string): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: jest.fn<() => Promise<string>>().mockResolvedValue(css),
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue({}),
    headers: new Headers({ "content-type": "text/css" }),
  } as unknown as Response;
}

/** Routes the three requests this feature's flow can ever issue (entry detail, scoped preview
 * compose, import) to scriptable responses — anything else throws, so a stray/unexpected request
 * (a font URL, a second catalog fetch) fails the test loudly rather than silently resolving. */
function themeFlowFetchMock(overrides: {
  entry?: Response;
  preview?: Response;
  importResponse?: Response;
} = {}): jest.MockedFunction<typeof fetch> {
  return jest.fn<typeof fetch>().mockImplementation(async (input) => {
    const url = String(input);
    if (url === ENTRY_URL) return overrides.entry ?? makeJsonResponse(200, THEME_DETAIL);
    if (url === PREVIEW_URL) return overrides.preview ?? makeCssResponse(200, SCOPED_PREVIEW_CSS);
    if (url === IMPORT_URL) {
      return (
        overrides.importResponse ??
        makeJsonResponse(200, {
          slug: "golden-frequency",
          name: "Golden Frequency",
          importedFrom: "golden-frequency",
          importedAt: "2026-08-06T00:00:00Z",
        })
      );
    }
    throw new Error(`unexpected fetch ${url}`);
  }) as unknown as jest.MockedFunction<typeof fetch>;
}

function cardFor(name: string): HTMLElement {
  const grid = screen.getByRole("list", { name: "Community catalog entries" });
  const nameNode = within(grid).getByText(name);
  const card = nameNode.closest("button");
  if (card === null) throw new Error(`No <button> ancestor for "${name}"`);
  return card;
}

/** Opens Golden Frequency's detail panel and waits for the live composed preview to render.
 * `installedThemeProvenance` (gh-#375) defaults to `[]`, the same "not installed" default
 * `PersonaCatalogClient`'s own prop carries — pass a row naming this entry's own slug to exercise
 * the already-installed path. */
async function openGoldenFrequencyPreview(
  fetchMock: jest.MockedFunction<typeof fetch>,
  installedThemeProvenance: { slug: string; importedFrom: string; importedAt: string }[] = []
): Promise<void> {
  global.fetch = fetchMock;
  render(
    <>
      <PersonaCatalogClient activeKind="theme"
        initialIndex={{ entries: [THEME_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        installedThemeProvenance={installedThemeProvenance}
        timeZone="UTC"
      />
      <Toaster />
    </>
  );
  fireEvent.click(cardFor("Golden Frequency"));
  await screen.findByTestId("theme-live-preview");
}

/** Carries `openGoldenFrequencyPreview` through to the open install-confirm dialog. */
async function openInstallDialog(fetchMock: jest.MockedFunction<typeof fetch>): Promise<void> {
  await openGoldenFrequencyPreview(fetchMock);
  fireEvent.click(screen.getByRole("button", { name: "Install" }));
  await screen.findByRole("dialog");
}

describe("Feature: previewing and installing a catalog theme", () => {
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

  describe("Scenario: the detail view previews the theme live", () => {
    it("composes the fetched manifest via ThemeCssComposer into a scoped preview container (T186, AC1)", async () => {
      const fetchMock = themeFlowFetchMock();
      await openGoldenFrequencyPreview(fetchMock);

      // The entry's raw manifest reached the preview-compose endpoint unchanged — never
      // re-derived or re-parsed client-side.
      const previewCall = fetchMock.mock.calls.find(([url]) => String(url) === PREVIEW_URL);
      expect(previewCall).toBeDefined();
      const [, init] = previewCall as [string, RequestInit];
      expect(init.method).toBe("POST");
      expect(init.body).toBe(GOLDEN_MANIFEST_JSON);

      // The composed CSS text lands verbatim in a <style> element scoped INSIDE the preview
      // container — never on document.documentElement/:root, and never re-interpreted.
      const container = screen.getByTestId("theme-live-preview");
      expect(container).toHaveClass("theme-live-preview");
      const style = container.querySelector("style");
      expect(style?.textContent).toBe(SCOPED_PREVIEW_CSS);
      expect(document.documentElement).not.toHaveAttribute("style");
    });

    // The AC2 "no new fonts" guarantee is NOT re-checked here (review finding N2): a check that
    // this component's own `fetch()` calls never include a "/fonts/" URL was vacuous — jsdom
    // never fetches the assets a stylesheet's own @font-face rules reference (a browser's CSS
    // engine does that outside `fetch()` entirely), so the assertion passed no matter what CSS
    // SCOPED_PREVIEW_CSS carried, including a hostile one. The real guarantee — the SAME
    // @font-face rules reach both the live and preview paths, because both run through the
    // identical `ThemeCssComposer` — is proven server-side, once, by
    // Story274_ThemeCatalogPreview.cs's own `CarriesTheSameFontFaceRulesAsTheLivePath`.
  });

  describe("Scenario: the scoped preview resolves the correct mode without leaking to :root (F1 fix)", () => {
    // A composed CSS fixture carrying the FIXED, ancestor-form selectors (F1 fix) — the mode
    // qualifier is an ancestor of the container, never compounded onto it, so getComputedStyle
    // below actually exercises the real cascade a browser resolves, not a hand-picked value.
    const SCOPED_PREVIEW_CSS_WITH_DARK = [
      ".theme-live-preview {",
      "  --bg: #f7ecd2;",
      "}",
      "",
      '[data-theme="dark"] .theme-live-preview {',
      "  --bg: #171205;",
      "}",
      "",
    ].join("\n");

    afterEach(() => {
      document.documentElement.removeAttribute("data-theme");
    });

    // jsdom (unlike a headless "check the CSS text" assertion) actually runs a real cascade
    // against the DOM `getComputedStyle` walks — so these two specs are the "true browser-
    // resolution assert" the F1 fix calls for, for the two cases jsdom can prove: an explicit
    // choice on <html> (jsdom applies attribute/descendant selectors correctly) and its absence.
    // jsdom never implements `prefers-color-scheme` media evaluation, so the OS-default case is
    // NOT provable this way — that block's own ANCESTOR-FORM STRUCTURE (not a compound glued onto
    // the container) is pinned instead, server-side, by
    // Story274_ThemeCatalogPreview.cs's own resolution-matrix scenario.
    it("resolves the theme's LIGHT token when the root carries no explicit data-theme", async () => {
      const fetchMock = themeFlowFetchMock({ preview: makeCssResponse(200, SCOPED_PREVIEW_CSS_WITH_DARK) });
      await openGoldenFrequencyPreview(fetchMock);

      const container = screen.getByTestId("theme-live-preview");
      expect(getComputedStyle(container).getPropertyValue("--bg").trim()).toBe("#f7ecd2");
    });

    it('resolves the theme\'s DARK token when the root carries an explicit data-theme="dark"', async () => {
      document.documentElement.setAttribute("data-theme", "dark");
      const fetchMock = themeFlowFetchMock({ preview: makeCssResponse(200, SCOPED_PREVIEW_CSS_WITH_DARK) });
      await openGoldenFrequencyPreview(fetchMock);

      const container = screen.getByTestId("theme-live-preview");
      expect(getComputedStyle(container).getPropertyValue("--bg").trim()).toBe("#171205");
    });
  });

  describe("Scenario: the served CSS's subject never drifts from the mirrored container class (N7)", () => {
    it("pins the composed CSS's own light-block subject to THEME_PREVIEW_CONTAINER_CLASS", () => {
      // A one-line drift guard: SCOPED_PREVIEW_CSS above is this file's own stand-in for "the
      // served CSS" every other spec here already exercises — this ties it to the TS constant
      // both ThemeDetailPreview.tsx and theme-preview.ts read, rather than a hardcoded string, so
      // the two can never silently drift apart.
      expect(SCOPED_PREVIEW_CSS.startsWith(`.${THEME_PREVIEW_CONTAINER_CLASS} {`)).toBe(true);
    });
  });

  describe("Scenario: confirming installs the theme", () => {
    it("posts the manifest to the import endpoint and the theme becomes available (T186, AC3)", async () => {
      const fetchMock = themeFlowFetchMock();
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      // Exactly one import POST, carrying the SAME manifest bytes the preview composed from, with
      // the entry's own slug threaded as both the route slug and ?catalogSlug (SPEC F90.7's
      // persona precedent, applied to themes).
      const importCalls = fetchMock.mock.calls.filter(([url]) => String(url) === IMPORT_URL);
      expect(importCalls).toHaveLength(1);
      const [, init] = importCalls[0] as [string, RequestInit];
      expect(init.method).toBe("POST");
      expect(init.body).toBe(GOLDEN_MANIFEST_JSON);

      // The dialog closes and a success toast confirms the install — the theme's own
      // selectability afterward is a server-side fact (Story272_ThemeImport.cs's
      // ScenarioTheAllowlistWidensAfterImport), not re-derived on this side of the wire.
      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      expect(await screen.findByText('"Golden Frequency" installed.')).toBeInTheDocument();
    });

    it("flips the detail panel to Installed/Re-install with the real provenance locally, no reload (gh-#375)", async () => {
      const fetchMock = themeFlowFetchMock();
      // Starts NOT installed — the default `installedThemeProvenance=[]` — so the button starts
      // "Install" and no provenance line renders yet.
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });
      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

      // The detail panel itself (still open — only the confirm dialog closed) now reads installed,
      // with the REAL provenance the import response carried (never a fabricated "just now") and
      // no second fetch: PersonaCatalogClient.handleThemeInstalled flips its own local state on the
      // toast, the same cheap path the font half's own T204 spec calls for.
      expect(screen.getByText("Installed")).toBeInTheDocument();
      expect(screen.getByText("Imported · golden-frequency · Aug 6, 2026")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Re-install" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Install" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: installed-state awareness (gh-#375 — the theme half of Dean's demo feedback)", () => {
    it("shows Install and no provenance line when the theme is not installed", async () => {
      const fetchMock = themeFlowFetchMock();
      await openGoldenFrequencyPreview(fetchMock);

      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
      expect(screen.queryByText("Installed")).not.toBeInTheDocument();
      expect(screen.queryByText(/^Imported ·/)).not.toBeInTheDocument();
    });

    it('shows an Installed chip, "Imported · <source> · <date>", and Re-install when the theme is already installed', async () => {
      const fetchMock = themeFlowFetchMock();
      await openGoldenFrequencyPreview(fetchMock, [
        { slug: "golden-frequency", importedFrom: "golden-frequency", importedAt: "2026-07-21T09:05:00Z" },
      ]);

      expect(screen.getByText("Installed")).toBeInTheDocument();
      expect(screen.getByText("Imported · golden-frequency · Jul 21, 2026")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Re-install" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Install" })).not.toBeInTheDocument();
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: cancelling installs nothing", () => {
    it("makes no import request and stores no theme when the owner cancels (T186, AC4)", async () => {
      const fetchMock = themeFlowFetchMock();
      await openInstallDialog(fetchMock);

      const callsBeforeCancel = fetchMock.mock.calls.length;
      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      // Cancel itself issues zero requests — no import, nothing else either.
      expect(fetchMock.mock.calls.length).toBe(callsBeforeCancel);
      expect(fetchMock.mock.calls.some(([url]) => String(url) === IMPORT_URL)).toBe(false);
    });
  });

  describe("Scenario: an unreachable entry degrades gracefully", () => {
    it("shows visible copy instead of crashing when the catalog entry is unreachable", async () => {
      const fetchMock = themeFlowFetchMock({
        entry: makeJsonResponse(200, {
          card: null,
          meta: null,
          fetchedAt: null,
          unreachable: true,
          audience: null,
          bestFor: null,
          author: null,
          description: null,
          samplePatter: null,
        }),
      });
      global.fetch = fetchMock;

      render(<PersonaCatalogClient activeKind="theme" initialIndex={{ entries: [THEME_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }} />);
      fireEvent.click(cardFor("Golden Frequency"));

      expect(await screen.findByText("Catalog unreachable — try again shortly.")).toBeInTheDocument();
      expect(screen.queryByTestId("theme-live-preview")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: an import 4xx degrades gracefully", () => {
    it("shows the server's error copy inside the still-open dialog, without crashing", async () => {
      const fetchMock = themeFlowFetchMock({
        importResponse: makeJsonResponse(409, { detail: '"golden-frequency" is a shipped theme\'s slug and cannot be overwritten (SPEC F103.8).' }),
      });
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      expect(await screen.findByRole("alert")).toHaveTextContent(
        '"golden-frequency" is a shipped theme\'s slug and cannot be overwritten (SPEC F103.8).'
      );
      // The dialog stays open — a failed confirm is not a crash, and the operator can still cancel.
      expect(screen.getByRole("dialog")).toBeInTheDocument();
    });

    it("flips nothing locally — the detail panel behind the dialog still reads Install, not Installed (gh-#375)", async () => {
      const fetchMock = themeFlowFetchMock({
        importResponse: makeJsonResponse(409, { detail: '"golden-frequency" is a shipped theme\'s slug and cannot be overwritten (SPEC F103.8).' }),
      });
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });
      await screen.findByRole("alert");

      // `onInstalled` (PersonaCatalogClient.handleThemeInstalled) only ever fires on
      // ThemeInstallModal's own 2xx branch — a 409 never reaches it, so the detail panel's own
      // Install button, still present behind the open dialog, never flips to Re-install.
      // `getByText`, not `getByRole` (Radix marks the background `aria-hidden` while the dialog is
      // open, which `*ByRole` correctly excludes but a plain text query does not).
      expect(screen.getByText("Install")).toBeInTheDocument();
      expect(screen.queryByText("Installed")).not.toBeInTheDocument();
    });
  });
});
