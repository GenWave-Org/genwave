// @jest-environment jsdom
// STORY-393 — Ad packs ride the shelf as data (SPEC F162.2 · PLAN T405)
//
// Runner: Jest. Mirrors font-pack-shelf-specimen.spec.tsx's own "shelf card is meta-only, detail
// panel pays the one per-entry fetch" idiom one kind over — the simplest kind yet: no specimen/asset
// fetch of any kind, the detail panel's whole job is listing `adPackBriefs` read-only. The install
// half mirrors icon-pack-renderer.spec.tsx's sibling install-modal idiom (IconInstallModal): confirm
// POSTs `POST /api/ad-packs/{slug}/install` with no body; cancel is a pure no-op.
//
// T405 review F6 widens this file: `adPackBriefs: null` (the manifest could not be read) and
// `adPackBriefs: []` (parsed, genuinely zero briefs) are two DIFFERENT wire states this panel must
// never conflate — see AD_PACK_DETAIL_UNPARSED/AD_PACK_DETAIL_EMPTY_BRIEFS's own remarks.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within, fireEvent, waitFor, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { Toaster } from "@/components/ui/toast";
import type { PersonaCatalogClient as PersonaCatalogClientType } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import type { CatalogEntryDetailDto, CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

// A dynamic, post-mock import (mirrors catalog-kind-tabs.spec.tsx's own idiom) — NOT a static
// top-level `import { PersonaCatalogClient } from ...`, which resolves "next/navigation"'s real
// `useRouter` before the `jest.mock` factory above ever takes effect and fails every render with
// "invariant expected app router to be mounted".
let PersonaCatalogClient: typeof PersonaCatalogClientType;

const AD_PACK_ENTRY: CatalogShelfEntryDto = {
  slug: "widget-world",
  kind: "ad-pack",
  audience: "everyone",
  bestFor: ["comedy"],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const AD_PACK_DETAIL: CatalogEntryDetailDto = {
  card: JSON.stringify({
    packName: "The Widget World Collection",
    briefs: [
      { brand: "Bramble & Fitch", premise: "A cozy hardware shop", tone: "warm", structure: "hook-offer-cta" },
      { brand: "Acme Filing Co", premise: "Bureaucracy, but faster", tone: null, structure: null },
    ],
  }),
  meta: "{}",
  fetchedAt: "2026-09-02T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: ["comedy"],
  author: null,
  description: "Parody brand briefs for a lighthearted break.",
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
  packName: "The Widget World Collection",
  iconCount: null,
  adPackBriefs: [
    { brand: "Bramble & Fitch", premise: "A cozy hardware shop", tone: "warm", structure: "hook-offer-cta" },
    { brand: "Acme Filing Co", premise: "Bureaucracy, but faster", tone: null, structure: null },
  ],
};

// packName absent, briefs still present and non-empty — isolates the TITLE fallback alone (T405
// review F6: this fixture used to also zero out adPackBriefs, conflating "no display name" with "no
// briefs," two independent, differently-caused states).
const AD_PACK_DETAIL_NO_PACK_NAME: CatalogEntryDetailDto = {
  ...AD_PACK_DETAIL,
  packName: null,
};

// A THIRD entry, distinct from both above, for the two F6 states below.
const EMPTY_BRIEFS_ENTRY: CatalogShelfEntryDto = { ...AD_PACK_ENTRY, slug: "empty-briefs-pack" };
const UNPARSED_ENTRY: CatalogShelfEntryDto = { ...AD_PACK_ENTRY, slug: "unparsed-pack" };

// `adPackBriefs: []` — the manifest PARSED successfully but declares zero briefs. Not actually
// reachable off the real wire today (CatalogAdPackManifestSerializer.Deserialize itself refuses a
// briefless manifest, degrading the WHOLE thing to `null` instead — see that type's own remarks),
// but the wire TYPE still allows it, and this panel stays honest to it defensively: Install stays
// enabled, the panel just names the empty list.
const AD_PACK_DETAIL_EMPTY_BRIEFS: CatalogEntryDetailDto = {
  ...AD_PACK_DETAIL,
  adPackBriefs: [],
};

// `adPackBriefs: null` — the manifest could NOT be read at all (malformed/hostile, or the entry was
// unreachable). AdPackController.Install would 400 on this SAME manifest, so the panel must not
// offer an Install that can only fail.
const AD_PACK_DETAIL_UNPARSED: CatalogEntryDetailDto = {
  ...AD_PACK_DETAIL,
  packName: null,
  adPackBriefs: null,
};

const ENTRY_URL = "/api/catalog/entries/widget-world";
const INSTALL_URL = "/api/ad-packs/widget-world/install";

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    text: jest.fn<() => Promise<string>>().mockResolvedValue(JSON.stringify(body)),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

function adPackFlowFetchMock(overrides: { entry?: Response; install?: Response } = {}): jest.MockedFunction<typeof fetch> {
  return jest.fn<typeof fetch>().mockImplementation(async (input) => {
    const url = String(input);
    if (url === ENTRY_URL) {
      return overrides.entry ?? makeJsonResponse(200, AD_PACK_DETAIL);
    }
    if (url === INSTALL_URL) {
      return (
        overrides.install ??
        makeJsonResponse(200, {
          slug: "widget-world",
          packName: "The Widget World Collection",
          brands: ["Bramble & Fitch", "Acme Filing Co"],
        })
      );
    }
    throw new Error(`unexpected fetch ${url}`);
  }) as unknown as jest.MockedFunction<typeof fetch>;
}

/** A one-off fetch mock that serves `detail` for whichever entry URL this render's own `entry.slug`
 * resolves to — used by the F6 render-state facts below, each with its own dedicated entry/slug. */
function detailOnlyFetchMock(entrySlug: string, detail: CatalogEntryDetailDto): jest.MockedFunction<typeof fetch> {
  return jest.fn<typeof fetch>().mockImplementation(async (input) => {
    const url = String(input);
    if (url === `/api/catalog/entries/${entrySlug}`) return makeJsonResponse(200, detail);
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

describe("Feature: ad packs ride the shelf as data", () => {
  let originalFetch: typeof fetch;

  beforeEach(async () => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ push: jest.fn() } as unknown as ReturnType<typeof useRouter>);
    ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  async function openWidgetWorldDetail(fetchMock: jest.MockedFunction<typeof fetch>): Promise<void> {
    global.fetch = fetchMock;
    render(
      <>
        <PersonaCatalogClient
          activeKind="ad-pack"
          initialIndex={{ entries: [AD_PACK_ENTRY], fetchedAt: "2026-09-02T00:00:00Z", unreachable: false }}
        />
        <Toaster />
      </>
    );
    fireEvent.click(cardFor("Widget World"));
    await screen.findByText("The Widget World Collection");
  }

  async function openInstallDialog(fetchMock: jest.MockedFunction<typeof fetch>): Promise<void> {
    await openWidgetWorldDetail(fetchMock);
    fireEvent.click(screen.getByRole("button", { name: "Install" }));
    await screen.findByRole("dialog");
  }

  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: the shelf card is meta-only", () => {
    it("renders the slug-derived title, the kind marker, and bestFor chips from the shelf payload alone (AC1)", () => {
      render(
        <PersonaCatalogClient
          activeKind="ad-pack"
          initialIndex={{ entries: [AD_PACK_ENTRY], fetchedAt: "2026-09-02T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Widget World")).toBeInTheDocument();
      expect(within(grid).getByText("Ad pack")).toBeInTheDocument();
      expect(within(grid).getByText("comedy")).toBeInTheDocument();
    });

    it("issues no manifest fetch on browse", () => {
      const fetchMock = jest.fn<typeof fetch>();
      global.fetch = fetchMock as unknown as typeof fetch;

      render(
        <PersonaCatalogClient
          activeKind="ad-pack"
          initialIndex={{ entries: [AD_PACK_ENTRY], fetchedAt: "2026-09-02T00:00:00Z", unreachable: false }}
        />
      );

      expect(fetchMock).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: the detail panel lists every brief read-only", () => {
    it("shows the pack's own display name and every brief's brand/premise/tone/structure (AC1)", async () => {
      const fetchMock = adPackFlowFetchMock();
      await openWidgetWorldDetail(fetchMock);

      expect(screen.getByText("Bramble & Fitch")).toBeInTheDocument();
      expect(screen.getByText("A cozy hardware shop")).toBeInTheDocument();
      expect(screen.getByText("warm · hook-offer-cta")).toBeInTheDocument();
      expect(screen.getByText("Acme Filing Co")).toBeInTheDocument();
      expect(screen.getByText("Bureaucracy, but faster")).toBeInTheDocument();
    });

    it("falls back to the slug-derived title when the manifest declares no pack name, briefs unaffected", async () => {
      const fetchMock = detailOnlyFetchMock(AD_PACK_ENTRY.slug, AD_PACK_DETAIL_NO_PACK_NAME);
      global.fetch = fetchMock;
      render(
        <PersonaCatalogClient
          activeKind="ad-pack"
          initialIndex={{ entries: [AD_PACK_ENTRY], fetchedAt: "2026-09-02T00:00:00Z", unreachable: false }}
        />
      );
      fireEvent.click(cardFor("Widget World"));
      await screen.findByText("Bramble & Fitch");

      const panel = screen.getByRole("region", { name: "Ad pack details" });
      // The slug-derived fallback ("Widget World") — NEVER the packName from a different fixture —
      // proves the fallback actually fired rather than coincidentally matching.
      expect(within(panel).getByText("Widget World")).toBeInTheDocument();
      expect(within(panel).queryByText("The Widget World Collection")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: parsed-empty vs unparseable are two different states (T405 review F6)", () => {
    it("names the pack as declaring no briefs, with Install still enabled, when the manifest parsed to zero briefs", async () => {
      const fetchMock = detailOnlyFetchMock(EMPTY_BRIEFS_ENTRY.slug, AD_PACK_DETAIL_EMPTY_BRIEFS);
      global.fetch = fetchMock;
      render(
        <PersonaCatalogClient
          activeKind="ad-pack"
          initialIndex={{ entries: [EMPTY_BRIEFS_ENTRY], fetchedAt: "2026-09-02T00:00:00Z", unreachable: false }}
        />
      );
      fireEvent.click(cardFor("Empty Briefs Pack"));

      expect(await screen.findByText("This pack declares no briefs.")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Install" })).toBeEnabled();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });

    it("names the manifest as unreadable, with Install DISABLED, when the manifest failed to parse at all", async () => {
      const fetchMock = detailOnlyFetchMock(UNPARSED_ENTRY.slug, AD_PACK_DETAIL_UNPARSED);
      global.fetch = fetchMock;
      render(
        <PersonaCatalogClient
          activeKind="ad-pack"
          initialIndex={{ entries: [UNPARSED_ENTRY], fetchedAt: "2026-09-02T00:00:00Z", unreachable: false }}
        />
      );
      fireEvent.click(cardFor("Unparsed Pack"));

      expect(await screen.findByRole("alert")).toHaveTextContent("could not be read");
      expect(screen.getByRole("button", { name: "Install" })).toBeDisabled();
      // The route would 400 on this exact manifest — the panel must never claim it has briefs to
      // show, nor the "no briefs" empty state (a DIFFERENT state — see the fact immediately above).
      expect(screen.queryByText("This pack declares no briefs.")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: confirming installs the pack", () => {
    it("posts exactly once to the install route with no body, and toasts the installed pack (AC2)", async () => {
      const fetchMock = adPackFlowFetchMock();
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      // AdPackController.Install takes no request body — every byte is fetched server-side, through
      // the guarded door, not supplied by this client.
      const installCalls = fetchMock.mock.calls.filter(([url]) => String(url) === INSTALL_URL);
      expect(installCalls).toHaveLength(1);
      const [, init] = installCalls[0] as [string, RequestInit];
      expect(init.method).toBe("POST");
      expect(init.body).toBeUndefined();

      await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
      expect(await screen.findByText('"The Widget World Collection" installed (2 briefs).')).toBeInTheDocument();
    });
  });

  describe("Scenario: the install dialog states the reinstall contract honestly (T405 review F5)", () => {
    it("names both halves — content refreshes, a disabled brief stays disabled", async () => {
      const fetchMock = adPackFlowFetchMock();
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      expect(dialog.getByText(/Reinstalling refreshes each brief's premise, tone, and structure text/)).toBeInTheDocument();
      expect(dialog.getByText(/a brief you've disabled stays disabled/)).toBeInTheDocument();
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: cancelling installs nothing", () => {
    it("makes no install request when the owner cancels", async () => {
      const fetchMock = adPackFlowFetchMock();
      await openInstallDialog(fetchMock);

      const callsBeforeCancel = fetchMock.mock.calls.length;
      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(fetchMock.mock.calls.length).toBe(callsBeforeCancel);
      expect(fetchMock.mock.calls.some(([url]) => String(url) === INSTALL_URL)).toBe(false);
    });
  });

  describe("Scenario: a failed install leaves the dialog open with an inline error", () => {
    it("shows the refusal message and never toasts", async () => {
      const fetchMock = adPackFlowFetchMock({
        install: makeJsonResponse(400, { detail: "\"widget-world\"'s ad manifest could not be parsed." }),
      });
      await openInstallDialog(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm install" }));
        await Promise.resolve();
      });

      expect(await screen.findByRole("alert")).toHaveTextContent("could not be parsed");
      expect(screen.queryByText(/installed/i)).not.toBeInTheDocument();
    });
  });
});
