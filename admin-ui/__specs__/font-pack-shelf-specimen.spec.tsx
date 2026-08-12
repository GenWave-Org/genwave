// @jest-environment jsdom
// STORY-281 — Packs on the shelf + the honest specimen (SPEC F104.3, F104.4 · PLAN T201/T202)
//
// Runner: Jest. T201 lands the shelf half: a font pack renders beside theme/persona entries on the
// SAME shelf, routed by `kind`, previewed from the index row's own `fontFamily`/`fontByteTotal`
// (T194) — no manifest fetch, no asset fetch, ever, while browsing. Mirrors
// theme-catalog-shelf.spec.tsx's own "renders from meta alone, fetches nothing while browsing"
// idiom for theme cards (T185). `description` is deliberately NOT asserted in the shelf scenario
// (STORY-281 AC1 reconciliation, T201): the shelf wire (`CatalogShelfEntryDto`) carries only
// `fontFamily`/`fontByteTotal` — `description` rides the per-entry detail fetch T202 builds.
//
// T202 lands the specimen half: opening a font card fetches the SAME per-entry detail every other
// kind uses (now carrying `fontFamily`/`fontSpecimenFile`/`description`, T194's font-kind
// projection) and routes to `FontDetailPanel`, whose `SpecimenBlock` loads the pack's real
// hash-verified woff2 face and applies it via the CSS Font Loading API. jsdom implements none of
// `FontFace`/`document.fonts`/`URL.createObjectURL` (probe-verified), so this file installs
// minimal, in-memory stand-ins for the three below — real enough to prove the PRODUCTION code's own
// add/delete and createObjectURL/revokeObjectURL calls are correctly paired, without needing an
// actual font decoder in a headless test run. T202 also lands the Install affordance
// (`FontInstallModal`, a stated scope addition — see `FontDetailPanel`'s own remarks): confirm
// POSTs `POST /api/fonts/{slug}/install` with no body; cancel is a pure no-op, the same idiom
// `theme-catalog-preview-install.spec.tsx` already pins for the theme kind (T186).
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
import { render, screen, within, fireEvent, waitFor, act } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { useRouter } from "next/navigation";
import { Toaster } from "@/components/ui/toast";
import type { PersonaCatalogClient as PersonaCatalogClientComponent } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import type { CatalogEntryDetailDto, CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let PersonaCatalogClient: typeof PersonaCatalogClientComponent;

beforeAll(async () => {
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
});

// ---------------------------------------------------------------------------
// Fixtures — the byte total (7844) is the SAME value
// tests/GenWave.Host.Tests/Fixtures/golden.font.json's "Space Grotesk" pack carries (T193/T194's
// golden parity precedent, theme-catalog-shelf.spec.tsx's own golden.theme.json precedent). The
// slug deliberately does NOT title-case to the same string as the family ("Libre Grotesk" vs "Space
// Grotesk") — a real pack's slug and its font's family name are two independently authored strings
// (FONTS.md), and keeping them distinct here lets each assertion below target one specific field
// without an ambiguous duplicate-text match (it also means the DETAIL panel's own title, "Libre
// Grotesk" via `prettifySlug(slug)`, never collides with the shelf card's "Space Grotesk" title or
// the detail panel's own "Family: Space Grotesk" line).
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

// The detail fetch's own font-kind projection (T194's `CatalogEntryResponse` widened by
// `FontFamily`/`FontByteTotal`/`FontSpecimenFile`) — `fontFamily`/`fontByteTotal` here are the
// DETAIL-side siblings of the shelf's own index-sourced fields of the same name (manifest-sourced,
// not index-sourced; a genuinely separate fetch), so this fixture repeats the same values on
// purpose (they agree on any real pack) rather than because the two wires are the same read.
const FONT_DETAIL: CatalogEntryDetailDto = {
  card: JSON.stringify({
    family: "Space Grotesk",
    files: [{ role: "upright", file: "libre-grotesk-variable-latin.woff2", weight: "300 700", style: "normal", bytes: 7844 }],
    license: "OFL-1.1",
  }),
  meta: "{}",
  fetchedAt: "2026-08-05T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: [],
  author: null,
  description: "A friendly grotesque built for headlines.",
  samplePatter: [],
  fontFamily: "Space Grotesk",
  fontByteTotal: 7844,
  fontSpecimenFile: "libre-grotesk-variable-latin.woff2",
  fontLicense: "OFL-1.1",
  fontVersion: "2.000",
  fontSubset: "latin",
};

// PLAN T204 (Dean's post-v3.1.0 review): "no mention of license anywhere in the panel" — the
// all-null edge, mirroring a manifest that failed to parse server-side (CatalogController's own
// degrade-not-500 posture).
const FONT_DETAIL_WITHOUT_LICENCE: CatalogEntryDetailDto = {
  ...FONT_DETAIL,
  fontLicense: null,
  fontVersion: null,
  fontSubset: null,
};

const ENTRY_URL = "/api/catalog/entries/libre-grotesk";
const ASSET_URL = "/api/catalog/entries/libre-grotesk/assets/libre-grotesk-variable-latin.woff2";
const INSTALL_URL = "/api/fonts/libre-grotesk/install";

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    text: jest.fn<() => Promise<string>>().mockResolvedValue(JSON.stringify(body)),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

function makeAssetResponse(status: number): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    blob: jest.fn<() => Promise<Blob>>().mockResolvedValue(new Blob(["fake-woff2-bytes"])),
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue({}),
    headers: new Headers({ "content-type": "font/woff2" }),
  } as unknown as Response;
}

/** Routes the three requests this feature's flow can ever issue (entry detail, specimen asset,
 * install) to scriptable responses — anything else throws, so a stray/unexpected request fails the
 * test loudly rather than silently resolving. */
function fontFlowFetchMock(
  overrides: { entry?: Response; asset?: Response; install?: Response } = {}
): jest.MockedFunction<typeof fetch> {
  return jest.fn<typeof fetch>().mockImplementation(async (input) => {
    const url = String(input);
    if (url === ENTRY_URL) return overrides.entry ?? makeJsonResponse(200, FONT_DETAIL);
    if (url === ASSET_URL) return overrides.asset ?? makeAssetResponse(200);
    if (url === INSTALL_URL) {
      return (
        overrides.install ??
        makeJsonResponse(200, {
          slug: "libre-grotesk",
          family: "Space Grotesk",
          faces: ["libre-grotesk-variable-latin.woff2"],
          importedFrom: "libre-grotesk",
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

/** A minimal `FontFace` stand-in (jsdom implements none of the CSS Font Loading API,
 * probe-verified) — just enough of the real shape for `SpecimenBlock`'s own `new FontFace(...)` +
 * `.load()` call to run, without a real font decoder. `load()` always resolves — the load-failure
 * branch this component also handles is exercised indirectly by the HTTP-failure spec below (the
 * fetch itself never reaches `FontFace` at all on that path). */
class MockFontFace {
  readonly family: string;
  readonly source: string;
  constructor(family: string, source: string) {
    this.family = family;
    this.source = source;
  }
  load(): Promise<FontFace> {
    return Promise.resolve(this as unknown as FontFace);
  }
}

describe("Feature: packs on the shelf with an honest specimen", () => {
  let originalFetch: typeof fetch;

  // T202's own transient-loading proof surface: real, in-memory stand-ins for
  // `document.fonts.add`/`.delete` and `URL.createObjectURL`/`.revokeObjectURL` — asserted on
  // directly (T201 review N4: "assert via document.fonts/injected style, not fetch counts"), never
  // via `fetchMock.mock.calls` counts alone.
  let addedFaces: FontFace[];
  let deletedFaces: FontFace[];
  let createdObjectUrls: string[];
  let revokedObjectUrls: string[];

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ push: jest.fn() } as unknown as ReturnType<typeof useRouter>);

    addedFaces = [];
    deletedFaces = [];
    createdObjectUrls = [];
    revokedObjectUrls = [];

    (globalThis as unknown as { FontFace: typeof FontFace }).FontFace = MockFontFace as unknown as typeof FontFace;

    (document as unknown as { fonts: { add: (face: FontFace) => void; delete: (face: FontFace) => boolean } }).fonts = {
      add: (face: FontFace) => {
        addedFaces.push(face);
      },
      delete: (face: FontFace) => {
        const index = addedFaces.indexOf(face);
        if (index === -1) return false;
        addedFaces.splice(index, 1);
        deletedFaces.push(face);
        return true;
      },
    };

    let objectUrlCounter = 0;
    URL.createObjectURL = jest.fn(() => {
      const url = `blob:mock-${objectUrlCounter++}`;
      createdObjectUrls.push(url);
      return url;
    }) as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = jest.fn((url: string) => {
      revokedObjectUrls.push(url);
    }) as unknown as typeof URL.revokeObjectURL;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  /** Opens Libre Grotesk's detail panel via the shelf card's own title text (the fontFamily "Space
   * Grotesk" — see this file's own fixture remarks for why it deliberately differs from the slug).
   * `installedFontSlugs` (PLAN T204) defaults to `[]`, the same "not installed" default
   * `PersonaCatalogClient`'s own prop carries — pass `["libre-grotesk"]` to exercise the
   * already-installed path. */
  async function openLibreGroteskDetail(
    fetchMock: jest.MockedFunction<typeof fetch>,
    installedFontSlugs: string[] = []
  ): Promise<void> {
    global.fetch = fetchMock;
    render(
      <>
        <PersonaCatalogClient activeKind="font"
          initialIndex={{ entries: [FONT_ENTRY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
          installedFontSlugs={installedFontSlugs}
        />
        <Toaster />
      </>
    );
    fireEvent.click(cardFor("Space Grotesk"));
  }

  /** Carries `openLibreGroteskDetail` through to the open install-confirm dialog. */
  async function openInstallDialog(fetchMock: jest.MockedFunction<typeof fetch>): Promise<void> {
    await openLibreGroteskDetail(fetchMock);
    await screen.findByTestId("font-specimen");
    fireEvent.click(screen.getByRole("button", { name: "Install" }));
    await screen.findByRole("dialog");
  }

  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: the shelf card is meta-only", () => {
    it("renders family and byte total from the shelf payload alone (T201, AC1)", () => {
      render(
        <PersonaCatalogClient activeKind="font"
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
        <PersonaCatalogClient activeKind="font"
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
        <PersonaCatalogClient activeKind="font"
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
        <PersonaCatalogClient activeKind="font"
          initialIndex={{ entries: [FONT_ENTRY_WITHOUT_FAMILY], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Libre Grotesk")).toBeInTheDocument();
    });

    it("degrades sanely, with no literal \"undefined\" text, when fontFamily/fontByteTotal are omitted from the wire rather than null (F2 review finding)", () => {
      render(
        <PersonaCatalogClient activeKind="font"
          initialIndex={{ entries: [FONT_ENTRY_WITH_UNDEFINED_FIELDS], fetchedAt: "2026-08-05T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Libre Grotesk")).toBeInTheDocument(); // title falls back to the slug
      expect(within(grid).queryByText(/undefined/i)).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the specimen is the real face", () => {
    it("renders the specimen in the pack's hash-verified face, loaded via document.fonts (T202, AC2)", async () => {
      const fetchMock = fontFlowFetchMock();
      await openLibreGroteskDetail(fetchMock);

      const specimen = await screen.findByTestId("font-specimen");

      // The real face reached document.fonts under a LOCAL, self-generated family name — never the
      // manifest's own "Space Grotesk" string (SpecimenBlock/FontDetailPanel's own remarks on the
      // T199/T200 stored-family obligation). Asserted on document.fonts itself (T201 review N4),
      // not on fetch call counts.
      expect(addedFaces).toHaveLength(1);
      expect(addedFaces[0]).toMatchObject({ family: "specimen-libre-grotesk" });

      // The specimen TEXT is actually SET in that same local family via an inline style — the face
      // reaching document.fonts alone would not prove it was ever applied to what the operator sees.
      const pangram = within(specimen).getByText("The quick brown fox jumps over the lazy dog");
      expect(pangram).toHaveStyle({ fontFamily: "specimen-libre-grotesk" });
    });

    it("discards everything on close — the face leaves document.fonts, the object URL is revoked, nothing installs (T202, AC2)", async () => {
      const fetchMock = fontFlowFetchMock();
      await openLibreGroteskDetail(fetchMock);
      await screen.findByTestId("font-specimen");

      expect(addedFaces).toHaveLength(1);
      expect(createdObjectUrls).toHaveLength(1);

      // Close = re-click the same card — the same collapse idiom every other kind's detail panel
      // already uses (handleCardClick), unmounting SpecimenBlock and running its own cleanup.
      fireEvent.click(cardFor("Space Grotesk"));

      expect(screen.queryByTestId("font-specimen")).not.toBeInTheDocument();
      expect(addedFaces).toHaveLength(0);
      expect(deletedFaces).toHaveLength(1);
      expect(revokedObjectUrls).toEqual(createdObjectUrls);

      // Discarding the specimen is a pure client-side read cleanup — it never touches the install
      // route, so nothing is installed and nothing is cached station-wide.
      expect(fetchMock.mock.calls.some(([url]) => String(url) === INSTALL_URL)).toBe(false);
    });
  });

  describe("Scenario: confirming installs the pack", () => {
    it("posts exactly once to the install route with no body, and toasts the installed family (T202, STORY-282 scope addition)", async () => {
      const fetchMock = fontFlowFetchMock();
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      // Exactly one install POST — FontPackController.Install takes no request body (SPEC F104.5's
      // "no request body, by design" rule): every byte is fetched server-side, through the guarded
      // door, not supplied by this client.
      const installCalls = fetchMock.mock.calls.filter(([url]) => String(url) === INSTALL_URL);
      expect(installCalls).toHaveLength(1);
      const [, init] = installCalls[0] as [string, RequestInit];
      expect(init.method).toBe("POST");
      expect(init.body).toBeUndefined();

      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      expect(await screen.findByText('"Space Grotesk" installed.')).toBeInTheDocument();
    });

    it("flips the detail panel to Installed/Re-install locally once the install succeeds, no reload (PLAN T204)", async () => {
      const fetchMock = fontFlowFetchMock();
      // Starts NOT installed — the default `installedFontSlugs=[]` — so the button starts "Install".
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });
      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

      // The detail panel itself (still open — only the confirm dialog closed) now reads installed,
      // with no second fetch and no page reload: PersonaCatalogClient.handleFontInstalled flips its
      // own local state on the toast, the cheap path this task's own spec calls for.
      expect(screen.getByText("Installed")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Re-install" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Install" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the licence is visible before install (PLAN T204, Dean's post-v3.1.0 review)", () => {
    it("shows the licence · version · subset line on the pre-install review panel", async () => {
      const fetchMock = fontFlowFetchMock();
      await openLibreGroteskDetail(fetchMock);

      expect(await screen.findByText("OFL-1.1 · v2.000 · latin")).toBeInTheDocument();
    });

    it("degrades to 'Licence unknown' rather than a blank line when the manifest carries none", async () => {
      const fetchMock = fontFlowFetchMock({ entry: makeJsonResponse(200, FONT_DETAIL_WITHOUT_LICENCE) });
      await openLibreGroteskDetail(fetchMock);

      expect(await screen.findByText("Licence unknown")).toBeInTheDocument();
    });
  });

  describe("Scenario: installed-state awareness (PLAN T204, Dean's post-v3.1.0 review)", () => {
    it("shows Install and a state-neutral specimen caption when the pack is not installed", async () => {
      const fetchMock = fontFlowFetchMock();
      await openLibreGroteskDetail(fetchMock);
      await screen.findByTestId("font-specimen");

      expect(screen.getByRole("button", { name: "Install" })).toBeInTheDocument();
      expect(screen.queryByText("Installed")).not.toBeInTheDocument();
      expect(screen.getByText("Transient specimen — previewing installs nothing")).toBeInTheDocument();
    });

    it("shows an Installed chip, Re-install, and the SAME neutral caption when the pack is already installed", async () => {
      const fetchMock = fontFlowFetchMock();
      await openLibreGroteskDetail(fetchMock, ["libre-grotesk"]);
      await screen.findByTestId("font-specimen");

      expect(screen.getByText("Installed")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Re-install" })).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Install" })).not.toBeInTheDocument();
      // The specimen caption never claims install state either way (F104.4's own "transient,
      // installs nothing" fact is true regardless) — see SpecimenBlock's own remarks.
      expect(screen.getByText("Transient specimen — previewing installs nothing")).toBeInTheDocument();
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: an unreachable asset degrades", () => {
    it("shows degraded copy without crashing on an integrity failure, and never adds a face (T202, AC3)", async () => {
      const fetchMock = fontFlowFetchMock({
        asset: makeJsonResponse(502, {
          detail: "This pack failed its integrity check and was withheld. Try again shortly.",
        }),
      });
      await openLibreGroteskDetail(fetchMock);

      expect(
        await screen.findByText("This pack failed its integrity check and was withheld. Try again shortly.")
      ).toBeInTheDocument();
      expect(screen.queryByTestId("font-specimen")).not.toBeInTheDocument();
      expect(addedFaces).toHaveLength(0);
      expect(createdObjectUrls).toHaveLength(0);

      // No crash, no partial state: the rest of the detail panel (fed by the ENTRY fetch, which
      // succeeded) is still there — only the specimen block itself degrades.
      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Space Grotesk")).toBeInTheDocument();
    });

    it("shows degraded copy without crashing when the catalog is unreachable, and never adds a face (T202, AC3)", async () => {
      const fetchMock = fontFlowFetchMock({
        asset: (() => {
          const resp = makeJsonResponse(503, { detail: "The catalog is currently unreachable. Try again shortly." });
          return resp;
        })(),
      });
      await openLibreGroteskDetail(fetchMock);

      expect(await screen.findByText("The catalog is currently unreachable. Try again shortly.")).toBeInTheDocument();
      expect(screen.queryByTestId("font-specimen")).not.toBeInTheDocument();
      expect(addedFaces).toHaveLength(0);
    });
  });

  describe("Scenario: cancelling installs nothing", () => {
    it("makes no install request when the owner cancels (T202, the T186 cancel-is-no-op idiom)", async () => {
      const fetchMock = fontFlowFetchMock();
      await openInstallDialog(fetchMock);

      const callsBeforeCancel = fetchMock.mock.calls.length;
      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      // Cancel itself issues zero requests — no install, nothing else either.
      expect(fetchMock.mock.calls.length).toBe(callsBeforeCancel);
      expect(fetchMock.mock.calls.some(([url]) => String(url) === INSTALL_URL)).toBe(false);
    });
  });

  describe("Scenario: a failed install flips nothing (gh-#375 review carry-forward, N3)", () => {
    it("flips nothing locally — the detail panel behind the dialog still reads Install, not Installed", async () => {
      const fetchMock = fontFlowFetchMock({
        install: makeJsonResponse(409, { detail: "This pack is already installed under a different family." }),
      });
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });
      await screen.findByRole("alert");

      // `onInstalled` (PersonaCatalogClient.handleFontInstalled) only ever fires on
      // FontInstallModal's own resp.ok branch — a 409 never reaches it, so the detail panel's own
      // Install button, still present behind the open dialog, never flips to Re-install.
      // `getByText`, not `getByRole` (Radix marks the background `aria-hidden` while the dialog is
      // open, which `*ByRole` correctly excludes but a plain text query does not).
      expect(screen.getByText("Install")).toBeInTheDocument();
      expect(screen.queryByText("Installed")).not.toBeInTheDocument();
    });
  });
});
