// @jest-environment jsdom
// STORY-472 — Install and Uninstall on the catalog entry (SPEC F204 · PLAN T564)
//
// Runner: Jest (jsdom). Turns every it.todo the /spec phase left behind into a passing `it`. Mirrors
// this feature's own already-shipped sibling files: ad-pack-shelf-install.spec.tsx and
// voice-jingle-pack-shelf-install.spec.tsx for the ConfirmDialogProvider + confirm-dialog ->
// DELETE -> toast idiom every pack-shaped kind now shares through the one `InstallToggle` (SPEC
// F204.1-F204.3), theme-catalog-preview-install.spec.tsx for the theme kind's own Install-only note
// (SPEC F204.4), and persona-catalog-page.spec.tsx for the server component's own fetch-wiring idiom
// (AC4) — `apiGet` prefixes every path with `BACKEND_URL`, so route assertions match on `endsWith`,
// never a bare path.
//
// House jest law for this file: exactly ONE `expect` per `it`; every arrange step (render, click,
// confirm, settle) lives in that Scenario's own `beforeAll`/`beforeEach`, never inside an `it`.

jest.mock("next/headers", () => ({
  cookies: jest.fn<() => Promise<{ toString: () => string }>>().mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import * as fs from "node:fs";
import * as path from "node:path";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import type { PersonaCatalogClient as PersonaCatalogClientComponent } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import { prettifySlug } from "../app/(authed)/persona-catalog/format-slug";
import type {
  CatalogEntryDetailDto,
  CatalogEntryKind,
  CatalogIndexResponseDto,
  CatalogShelfEntryDto,
} from "../app/(authed)/persona-catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// A dynamic, post-mock import (mirrors persona-catalog-page.spec.tsx's own idiom) — a static
// top-level `import { PersonaCatalogClient } from ...` would bind "next/navigation"'s REAL
// `useRouter` before the `jest.mock` factory above ever runs (this project's SWC-based jest
// transform does not hoist `jest.mock` past a static import the way babel-jest does).
let PersonaCatalogClient: typeof PersonaCatalogClientComponent;

beforeAll(async () => {
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
});

// ---------------------------------------------------------------------------
// Shared fetch/response helpers (mirror voice-jingle-pack-shelf-install.spec.tsx's own idioms)
// ---------------------------------------------------------------------------

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response;
}

function make204(): Response {
  return { ok: true, status: 204, json: jest.fn<() => Promise<unknown>>().mockResolvedValue(undefined) } as unknown as Response;
}

function makeAssetResponse(): Response {
  return { ok: true, status: 200, blob: jest.fn<() => Promise<Blob>>().mockResolvedValue(new Blob(["fake-mp3"])) } as unknown as Response;
}

/** Finds the shelf card `<button>` for a given entry's displayed (prettified) name — scoped to the
 * entries grid, mirrors persona-catalog-page.spec.tsx's own `cardFor` helper verbatim. */
function cardFor(name: string): HTMLElement {
  const grid = screen.getByRole("list", { name: "Community catalog entries" });
  const nameNode = within(grid).getByText(name);
  const card = nameNode.closest("button");
  if (card === null) throw new Error(`No <button> ancestor for "${name}"`);
  return card;
}

/** One real macrotask — every microtask already queued (a DELETE's own await chain: reading the
 * response, toasting, flipping local state) drains before this timeout fires. */
function flush(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/** Every field `CatalogEntryDetailDto` declares that this feature's own fixtures never vary —
 * callers override only `card` (and, for the ad-pack/font/etc. panels that read a field off this
 * shape directly rather than off `card`, the one or two fields they need). */
const BASE_DETAIL: CatalogEntryDetailDto = {
  card: "{}",
  meta: "{}",
  fetchedAt: "2026-09-23T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: [],
  author: null,
  description: null,
  samplePatter: [],
  fontFamily: null,
  fontByteTotal: null,
  fontSpecimenFile: null,
  fontLicense: null,
  fontVersion: null,
  fontSubset: null,
  suggestedPersona: null,
  avatarItems: null,
  personaAvatarFile: null,
  packName: null,
  iconCount: null,
  adPackBriefs: null,
};

function makeEntry(kind: CatalogEntryKind, slug: string): CatalogShelfEntryDto {
  return { slug, kind, audience: "everyone", bestFor: [], preview: null, fontFamily: null, fontByteTotal: null };
}

describe("Feature: Install and Uninstall on the catalog entry", () => {
  let originalFetch: typeof fetch;
  let refreshMock: jest.Mock;

  beforeEach(() => {
    originalFetch = global.fetch;
    refreshMock = jest.fn();
    mockedUseRouter.mockReturnValue({ push: jest.fn(), refresh: refreshMock } as unknown as ReturnType<typeof useRouter>);

    let counter = 0;
    URL.createObjectURL = jest.fn(() => `blob:mock-${counter++}`) as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = jest.fn() as unknown as typeof URL.revokeObjectURL;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  // AC1 / AC2 — the toggle's two states
  // -------------------------------------------------------------------------

  const TOGGLE_FONT_SLUG = "libre-grotesk";
  const TOGGLE_FONT_ENTRY = makeEntry("font", TOGGLE_FONT_SLUG);
  const TOGGLE_FONT_DETAIL: CatalogEntryDetailDto = { ...BASE_DETAIL, fontFamily: "Libre Grotesk" };
  const TOGGLE_FONT_DISPLAY_NAME = prettifySlug(TOGGLE_FONT_SLUG);

  function fontEntryFetchMock(): jest.MockedFunction<typeof fetch> {
    return jest.fn<typeof fetch>().mockImplementation(async (input) => {
      const url = String(input);
      if (url === `/api/catalog/entries/${TOGGLE_FONT_SLUG}`) return makeJsonResponse(200, TOGGLE_FONT_DETAIL);
      throw new Error(`unexpected fetch ${url}`);
    }) as unknown as jest.MockedFunction<typeof fetch>;
  }

  describe("Scenario: a font not installed", () => {
    // Given: installed set without the slug
    beforeEach(async () => {
      global.fetch = fontEntryFetchMock();
      render(
        <ConfirmDialogProvider>
          <PersonaCatalogClient
            activeKind="font"
            initialIndex={{ entries: [TOGGLE_FONT_ENTRY], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false }}
            installedFontSlugs={[]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );
      fireEvent.click(cardFor(TOGGLE_FONT_DISPLAY_NAME));
      await screen.findByRole("button", { name: "Install" });
    });

    it("AC1 — the toggle reads \"Install\"", () => {
      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
    });

    it("AC1 — no \"Uninstall\" button renders", () => {
      expect(screen.queryByRole("button", { name: "Uninstall" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a font installed", () => {
    // Given: installed set with the slug
    beforeEach(async () => {
      global.fetch = fontEntryFetchMock();
      render(
        <ConfirmDialogProvider>
          <PersonaCatalogClient
            activeKind="font"
            initialIndex={{ entries: [TOGGLE_FONT_ENTRY], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false }}
            installedFontSlugs={[TOGGLE_FONT_SLUG]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );
      fireEvent.click(cardFor(TOGGLE_FONT_DISPLAY_NAME));
      await screen.findByRole("button", { name: "Uninstall" });
    });

    it("AC2 — the toggle reads \"Uninstall\"", () => {
      expect(screen.getByRole("button", { name: "Uninstall" })).toBeInTheDocument();
    });

    it("AC2 — no \"Install\" button renders", () => {
      expect(screen.queryByRole("button", { name: "Install" })).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // AC3 — one installed row per pack-shaped kind
  // -------------------------------------------------------------------------

  type InstalledSlugsPropName =
    | "installedFontSlugs"
    | "installedIconSlugs"
    | "installedAvatarSlugs"
    | "installedAdPackSlugs"
    | "installedVoicePackSlugs"
    | "installedJinglePackSlugs";

  function installedSlugsProp(key: InstalledSlugsPropName, slug: string): Partial<Record<InstalledSlugsPropName, string[]>> {
    return { [key]: [slug] };
  }

  interface Ac3Case {
    kind: "font" | "icon" | "avatar" | "ad-pack" | "jingle-pack" | "voice-pack";
    slug: string;
    installedPropKey: InstalledSlugsPropName;
    card: string;
    extraRoutes?: Record<string, Response>;
  }

  const AC3_CASES: readonly Ac3Case[] = [
    { kind: "font", slug: "quiet-serif", installedPropKey: "installedFontSlugs", card: "{}" },
    { kind: "icon", slug: "gw-icons", installedPropKey: "installedIconSlugs", card: "{}" },
    { kind: "avatar", slug: "gw-avatars", installedPropKey: "installedAvatarSlugs", card: "{}" },
    { kind: "ad-pack", slug: "widget-world", installedPropKey: "installedAdPackSlugs", card: "{}" },
    {
      kind: "jingle-pack",
      slug: "sunday-drive-jingles",
      installedPropKey: "installedJinglePackSlugs",
      card: JSON.stringify({
        packName: "Sunday Drive Jingles",
        assets: [{ file: "station-id-chime.mp3", role: "station_id", title: "Station Chime", license: "CC0" }],
      }),
    },
    {
      kind: "voice-pack",
      slug: "moonlit-narrators",
      installedPropKey: "installedVoicePackSlugs",
      card: JSON.stringify({
        packName: "Moonlit Narrators",
        engine: "kokoro",
        preview: "moonlit-preview.mp3",
        voices: [{ voiceId: "af_heart", file: "af_heart.pt" }],
      }),
      extraRoutes: { "/api/catalog/entries/moonlit-narrators/assets/moonlit-preview.mp3": makeAssetResponse() },
    },
  ];

  describe.each(AC3_CASES)("Scenario: an installed $kind row", (testCase) => {
    // Given: this kind's own installed slug
    const { kind, slug, installedPropKey, card, extraRoutes } = testCase;
    const entry = makeEntry(kind, slug);
    const detail: CatalogEntryDetailDto = { ...BASE_DETAIL, card };
    const entryUrl = `/api/catalog/entries/${slug}`;

    beforeEach(async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === entryUrl) return makeJsonResponse(200, detail);
        if (extraRoutes) {
          const match = extraRoutes[url];
          if (match !== undefined) return match;
        }
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <ConfirmDialogProvider>
          <PersonaCatalogClient
            activeKind={kind}
            initialIndex={{ entries: [entry], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false }}
            {...installedSlugsProp(installedPropKey, slug)}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );
      fireEvent.click(cardFor(prettifySlug(slug)));
      await screen.findByRole("button", { name: "Uninstall" });
    });

    it("AC3 — the row shows \"Uninstall\" once its slug is installed", () => {
      expect(screen.getByRole("button", { name: "Uninstall" })).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // AC4 — the server component fetches all six installed-slug listings
  // -------------------------------------------------------------------------

  describe("Scenario: the server component", () => {
    // Given: persona-catalog/page.tsx with fetch mocked, called once
    let calledUrls: string[];

    beforeAll(async () => {
      const indexBody: CatalogIndexResponseDto = { entries: [], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false };
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url.endsWith("/api/catalog/index")) return makeJsonResponse(200, indexBody);
        // Every other leg of the page's own Promise.all (fonts/icon-packs/avatar-packs/ad-briefs/
        // jingle-packs/voice-packs/settings/shows/personas) degrades to [] on a non-array body, so
        // one catch-all 200 empty array covers all of them — this test only cares WHICH routes were
        // asked for, not what they returned.
        return makeJsonResponse(200, []);
      }) as unknown as typeof fetch;
      global.fetch = fetchMock;

      const { default: PersonaCatalogPage } = await import("../app/(authed)/persona-catalog/page");
      await PersonaCatalogPage({ searchParams: Promise.resolve({}) });

      calledUrls = (fetchMock as jest.MockedFunction<typeof fetch>).mock.calls.map(([input]) => String(input));
    });

    it.each(["/api/fonts", "/api/icon-packs", "/api/avatar-packs", "/api/ad-briefs", "/api/jingle-packs", "/api/voice-packs"])(
      "AC4 — fetches %s",
      (route) => {
        expect(calledUrls.some((url) => url.endsWith(route))).toBe(true);
      }
    );
  });

  // -------------------------------------------------------------------------
  // AC5 / AC6 / AC7 — a confirmed uninstall of an icon
  // -------------------------------------------------------------------------

  describe("Scenario: a confirmed uninstall of an icon", () => {
    // Given: DELETE mocked 204, ConfirmDialogProvider + Toaster
    const ICON_SLUG = "gw-icons";
    const ICON_ENTRY = makeEntry("icon", ICON_SLUG);
    const ICON_DETAIL: CatalogEntryDetailDto = { ...BASE_DETAIL };
    const ICON_DISPLAY_NAME = prettifySlug(ICON_SLUG);
    const ICON_ENTRY_URL = `/api/catalog/entries/${ICON_SLUG}`;
    const ICON_DELETE_URL = `/api/icon-packs/${ICON_SLUG}`;

    let fetchMock: jest.MockedFunction<typeof fetch>;

    beforeEach(async () => {
      fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url === ICON_ENTRY_URL) return makeJsonResponse(200, ICON_DETAIL);
        if (url === ICON_DELETE_URL && method === "DELETE") return make204();
        throw new Error(`unexpected fetch ${url} ${method}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <ConfirmDialogProvider>
          <PersonaCatalogClient
            activeKind="icon"
            initialIndex={{ entries: [ICON_ENTRY], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false }}
            installedIconSlugs={[ICON_SLUG]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );
      fireEvent.click(cardFor(ICON_DISPLAY_NAME));
      fireEvent.click(await screen.findByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
        await Promise.resolve();
      });
      // Settle on the DELETE round trip itself, not the toast (AC7's own proof) — waiting for the
      // fetch mock call plus one real macrotask flush lets every awaited step in InstallToggle's own
      // handleUninstall (the response read, the toast, the local flip) land before any assertion runs.
      await waitFor(() => {
        expect(fetchMock.mock.calls.some(([input, init]) => String(input) === ICON_DELETE_URL && init?.method === "DELETE")).toBe(
          true
        );
      });
      await act(async () => {
        await flush();
      });
    });

    it("AC5 — DELETE /api/icon-packs/{slug} is called once", () => {
      const deleteCalls = fetchMock.mock.calls.filter(
        ([input, init]) => String(input) === ICON_DELETE_URL && init?.method === "DELETE"
      );
      expect(deleteCalls).toHaveLength(1);
    });

    it("AC6 — the row reads \"Install\" without a reload", () => {
      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
    });

    it("AC6 — no \"Uninstall\" button renders", () => {
      expect(screen.queryByRole("button", { name: "Uninstall" })).not.toBeInTheDocument();
    });

    it("AC7 — a toast names the pack", () => {
      expect(screen.getByText(`"${ICON_DISPLAY_NAME}" uninstalled.`)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // An ad-pack uninstall that keeps a sponsor (200)
  // -------------------------------------------------------------------------

  describe("Scenario: an ad-pack uninstall keeps a sponsor (200)", () => {
    // Given: DELETE mocked 200 with a non-empty keptSponsors body (AdPackController.Uninstall)
    const KEPT_SLUG = "kept-sponsor-pack";
    const KEPT_ENTRY = makeEntry("ad-pack", KEPT_SLUG);
    const KEPT_DETAIL: CatalogEntryDetailDto = { ...BASE_DETAIL };
    const KEPT_DISPLAY_NAME = prettifySlug(KEPT_SLUG);
    const KEPT_ENTRY_URL = `/api/catalog/entries/${KEPT_SLUG}`;
    const KEPT_DELETE_URL = `/api/ad-packs/${KEPT_SLUG}`;

    beforeEach(async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url === KEPT_ENTRY_URL) return makeJsonResponse(200, KEPT_DETAIL);
        if (url === KEPT_DELETE_URL && method === "DELETE") {
          return makeJsonResponse(200, { slug: KEPT_SLUG, retiredSpots: 1, keptSponsors: [{ id: 1, name: "Acme", paused: false }] });
        }
        throw new Error(`unexpected fetch ${url} ${method}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <ConfirmDialogProvider>
          <PersonaCatalogClient
            activeKind="ad-pack"
            initialIndex={{ entries: [KEPT_ENTRY], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false }}
            installedAdPackSlugs={[KEPT_SLUG]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );
      fireEvent.click(cardFor(KEPT_DISPLAY_NAME));
      fireEvent.click(await screen.findByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
        await Promise.resolve();
      });
      await waitFor(() => {
        expect(fetchMock.mock.calls.some(([input, init]) => String(input) === KEPT_DELETE_URL && init?.method === "DELETE")).toBe(
          true
        );
      });
      await act(async () => {
        await flush();
      });
    });

    it("a 200 with kept sponsors still flips the row to \"Install\"", () => {
      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
    });

    it("a 200 with kept sponsors names the survivor in the toast", () => {
      expect(screen.getByText(`"${KEPT_DISPLAY_NAME}" uninstalled. Kept sponsor: Acme.`)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // A pack already uninstalled elsewhere (404)
  // -------------------------------------------------------------------------

  describe("Scenario: a pack already uninstalled elsewhere (404)", () => {
    // Given: DELETE mocked 404 — a stale local "installed" flag pointing at an already-deleted pack
    const GONE_SLUG = "already-gone-icons";
    const GONE_ENTRY = makeEntry("icon", GONE_SLUG);
    const GONE_DETAIL: CatalogEntryDetailDto = { ...BASE_DETAIL };
    const GONE_DISPLAY_NAME = prettifySlug(GONE_SLUG);
    const GONE_ENTRY_URL = `/api/catalog/entries/${GONE_SLUG}`;
    const GONE_DELETE_URL = `/api/icon-packs/${GONE_SLUG}`;

    beforeEach(async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url === GONE_ENTRY_URL) return makeJsonResponse(200, GONE_DETAIL);
        if (url === GONE_DELETE_URL && method === "DELETE") return makeJsonResponse(404, { detail: "Not found." });
        throw new Error(`unexpected fetch ${url} ${method}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <ConfirmDialogProvider>
          <PersonaCatalogClient
            activeKind="icon"
            initialIndex={{ entries: [GONE_ENTRY], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false }}
            installedIconSlugs={[GONE_SLUG]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );
      fireEvent.click(cardFor(GONE_DISPLAY_NAME));
      fireEvent.click(await screen.findByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
        await Promise.resolve();
      });
      await waitFor(() => {
        expect(fetchMock.mock.calls.some(([input, init]) => String(input) === GONE_DELETE_URL && init?.method === "DELETE")).toBe(
          true
        );
      });
      await act(async () => {
        await flush();
      });
    });

    it("a 404 still flips the row to \"Install\"", () => {
      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // AC10 — a theme row (Install only, Theme Editor note)
  // -------------------------------------------------------------------------

  describe("Scenario: a theme row", () => {
    // Given: a theme catalog entry
    const THEME_SLUG = "golden-frequency";
    const THEME_MANIFEST_JSON = JSON.stringify({
      slug: THEME_SLUG,
      name: "Golden Frequency",
      author: "GenWave",
      fonts: {
        display: { family: "Fraunces", assets: [{ src: "/fonts/fraunces-variable-latin.woff2", weight: "400 600", style: "normal" }] },
        sans: { family: "Source Sans 3", assets: [{ src: "/fonts/source-sans-3-variable-latin.woff2", weight: "400", style: "normal" }] },
      },
      modes: { light: { bg: "#f7ecd2", ink: "#2c2410" }, dark: { bg: "#171205", ink: "#f4ecce" } },
    });
    const THEME_ENTRY = makeEntry("theme", THEME_SLUG);
    const THEME_DETAIL: CatalogEntryDetailDto = { ...BASE_DETAIL, card: THEME_MANIFEST_JSON };
    const THEME_DISPLAY_NAME = prettifySlug(THEME_SLUG);
    const THEME_ENTRY_URL = `/api/catalog/entries/${THEME_SLUG}`;
    const THEME_PREVIEW_URL = "/api/themes/preview";
    const THEME_NOTE_TEXT = "Imported themes are managed on the Theme Editor.";

    function makeCssResponse(status: number, css: string): Response {
      return { ok: status >= 200 && status < 300, status, text: jest.fn<() => Promise<string>>().mockResolvedValue(css) } as unknown as Response;
    }

    beforeEach(async () => {
      const fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = String(input);
        if (url === THEME_ENTRY_URL) return makeJsonResponse(200, THEME_DETAIL);
        if (url === THEME_PREVIEW_URL) return makeCssResponse(200, ".theme-live-preview { --bg: #f7ecd2; }");
        throw new Error(`unexpected fetch ${url}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      // No ConfirmDialogProvider ancestor — ThemeDetailPanel never renders InstallToggle (SPEC
      // F204.4), so it never calls useConfirm() and needs no provider.
      render(
        <PersonaCatalogClient
          activeKind="theme"
          initialIndex={{ entries: [THEME_ENTRY], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false }}
        />
      );
      fireEvent.click(cardFor(THEME_DISPLAY_NAME));
      await screen.findByText(THEME_NOTE_TEXT);
    });

    it("AC10 — shows Install only", () => {
      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
    });

    it("AC10 — no Uninstall button renders", () => {
      expect(screen.queryByRole("button", { name: "Uninstall" })).not.toBeInTheDocument();
    });

    it("AC10 — shows \"Imported themes are managed on the Theme Editor\"", () => {
      expect(screen.getByText(THEME_NOTE_TEXT)).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  // AC13 — the retired prose parser is gone
  // -------------------------------------------------------------------------

  /** Walks `root` looking for a file named `name`, skipping `node_modules`/`.next` — the same
   * directories a real build never scans into either. Throws when `root` itself doesn't exist
   * — a wrong/misspelled root must fail the scan loudly, never pass
   * vacuously by finding nothing to look through. */
  function containsFile(root: string, name: string): boolean {
    if (!fs.existsSync(root)) throw new Error(`AC13 scan root does not exist: ${root}`);
    for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
      if (entry.name === "node_modules" || entry.name === ".next") continue;
      const full = path.join(root, entry.name);
      if (entry.isDirectory()) {
        if (containsFile(full, name)) return true;
      } else if (entry.name === name) {
        return true;
      }
    }
    return false;
  }

  describe("Scenario: the source tree", () => {
    // Given: admin-ui/app and admin-ui/lib scanned recursively
    it("AC13 — referenced-themes.ts no longer exists", () => {
      const roots = [path.join(__dirname, "..", "app"), path.join(__dirname, "..", "lib")];
      const found = roots.some((root) => containsFile(root, "referenced-themes.ts"));
      expect(found).toBe(false);
    });
  });

  // -------------------------------------------------------------------------
  // AC11 / AC12 — sad path: a font a theme still references
  // -------------------------------------------------------------------------

  describe("Scenario: a font a theme references", () => {
    // Given: DELETE mocked 409 with extensions.referencedBy ["Night Owl"]
    const REFERENCED_FONT_SLUG = "midnight-condensed";
    const REFERENCED_FONT_ENTRY = makeEntry("font", REFERENCED_FONT_SLUG);
    const REFERENCED_FONT_DETAIL: CatalogEntryDetailDto = { ...BASE_DETAIL };
    const REFERENCED_FONT_DISPLAY_NAME = prettifySlug(REFERENCED_FONT_SLUG);
    const REFERENCED_FONT_ENTRY_URL = `/api/catalog/entries/${REFERENCED_FONT_SLUG}`;
    const REFERENCED_FONT_DELETE_URL = `/api/fonts/${REFERENCED_FONT_SLUG}`;
    const EXPECTED_TOAST = "Cannot uninstall — still used by Night Owl.";

    let fetchMock: jest.MockedFunction<typeof fetch>;

    beforeEach(async () => {
      fetchMock = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
        const url = String(input);
        const method = init?.method ?? "GET";
        if (url === REFERENCED_FONT_ENTRY_URL) return makeJsonResponse(200, REFERENCED_FONT_DETAIL);
        if (url === REFERENCED_FONT_DELETE_URL && method === "DELETE") {
          return makeJsonResponse(409, { detail: "Font pack is referenced.", referencedBy: ["Night Owl"] });
        }
        throw new Error(`unexpected fetch ${url} ${method}`);
      }) as unknown as jest.MockedFunction<typeof fetch>;
      global.fetch = fetchMock;

      render(
        <ConfirmDialogProvider>
          <PersonaCatalogClient
            activeKind="font"
            initialIndex={{ entries: [REFERENCED_FONT_ENTRY], fetchedAt: "2026-09-23T00:00:00Z", unreachable: false }}
            installedFontSlugs={[REFERENCED_FONT_SLUG]}
          />
          <Toaster />
        </ConfirmDialogProvider>
      );
      fireEvent.click(cardFor(REFERENCED_FONT_DISPLAY_NAME));
      fireEvent.click(await screen.findByRole("button", { name: "Uninstall" }));
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Uninstall" }));
        await Promise.resolve();
      });
      // Settle on the DELETE round trip itself, not the toast (AC12's own proof) — mirrors the icon
      // uninstall scenario's own arrange above.
      await waitFor(() => {
        expect(
          fetchMock.mock.calls.some(([input, init]) => String(input) === REFERENCED_FONT_DELETE_URL && init?.method === "DELETE")
        ).toBe(true);
      });
      await act(async () => {
        await flush();
      });
    });

    it("AC11 — the row still reads \"Uninstall\"", () => {
      expect(screen.getByRole("button", { name: "Uninstall" })).toBeInTheDocument();
    });

    it("AC11 — router.refresh() is never called", () => {
      expect(refreshMock).not.toHaveBeenCalled();
    });

    it("AC12 — the toast contains \"Night Owl\"", () => {
      expect(screen.getByText(EXPECTED_TOAST)).toBeInTheDocument();
    });
  });
});
