// @jest-environment jsdom
// STORY-392 — I manage the Ads library from one page (page half: AC1–AC5 · F162.1 · PLAN T404)
// The API half lives in tests/GenWave.Host.Tests/Specs/Story392_AdsApi.cs.
//
// Runner: Jest (jsdom) + @testing-library/react — the Gardener page's own two-file split (T387's
// `gardener-page.spec.tsx` renders `GardenerSection` directly, props-driven; `gardener-tabs.spec.tsx`
// drives the real server `page.tsx` end-to-end) collapsed into this ONE file, since T404's whole
// pending suite lives here: page-level wiring (state fan-out, the tab strip, the pager/size picker)
// drives the real `ads/page.tsx` the same way `gardener-tabs.spec.tsx` drives `gardener/page.tsx`
// (`next/headers`'s `cookies()` and `next/navigation`'s `useRouter` mocked, `global.fetch` mocked by
// method+pathname, the page `await import()`ed fresh after the mocks are registered — this
// project's SWC jest transform does not hoist `jest.mock` past a static import); everything else
// (the editor, row verbs, the ready-spot preview, the briefs tab) renders its own client component
// directly with RTL, mirroring `gardener-page.spec.tsx`'s `renderSection` posture.
//
// One design decision this suite pins (PLAN T404's own judgment call, documented at its source too
// — `AdsTabs.tsx`):
//   - Tabs are deliberately UNBADGED (no `/api/status` ads block exists to badge them from honestly
//     without a six-call fan-out) — the active tab's own EXACT total renders in its section header
//     instead. The "badges every state tab with its count" title from the original pending stub is
//     replaced with "shows the active tab's own total, leaving every tab unbadged" to match.
//   - A `ready` spot's "Preview" now reveals a real `<audio>` player (PLAN T404b: `GET
//     /api/media/{id}/audio` now streams the persisted bytes — see `AdSpotRow.tsx`'s own remarks).
//     T404's own "reveals an honest no-playback-yet notice" title is retired; the original pending
//     stub's title ("plays the rendered artifact in the browser") is restored below, now true.

jest.mock("next/headers", () => ({
  cookies: jest
    .fn<() => Promise<{ toString: () => string }>>()
    .mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { cleanup, render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { PageSizePicker } from "@/components/ui/page-size-picker";
import { Toaster } from "@/components/ui/toast";
import type { AdBriefDto, AdSpotDto, AdState } from "@/lib/ads-api";
import { ADS_PAGE_SIZES, buildAdsHref, DEFAULT_ADS_PAGE_SIZE } from "../app/(authed)/ads/ads-paging";
import type { AdsSection as AdsSectionComponent } from "../app/(authed)/ads/AdsSection";
import type { AdSpotEditor as AdSpotEditorComponent } from "../app/(authed)/ads/AdSpotEditor";
import type { BriefsSection as BriefsSectionComponent } from "../app/(authed)/ads/BriefsSection";
import type { AdsTabs as AdsTabsComponent } from "../app/(authed)/ads/AdsTabs";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedRefresh = jest.fn<() => void>();

// Every module under test calls `useRouter()` (directly or transitively) at render time, so each
// must be `import()`ed AFTER the mock above is registered — a static top-level import would bind
// the REAL `next/navigation` export first (see the file header remarks).
let AdsSection: typeof AdsSectionComponent;
let AdSpotEditor: typeof AdSpotEditorComponent;
let BriefsSection: typeof BriefsSectionComponent;
let AdsTabs: typeof AdsTabsComponent;

beforeAll(async () => {
  ({ AdsSection } = await import("../app/(authed)/ads/AdsSection"));
  ({ AdSpotEditor } = await import("../app/(authed)/ads/AdSpotEditor"));
  ({ BriefsSection } = await import("../app/(authed)/ads/BriefsSection"));
  ({ AdsTabs } = await import("../app/(authed)/ads/AdsTabs"));
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function adSpot(overrides: Partial<AdSpotDto> = {}): AdSpotDto {
  return {
    id: 1,
    brand: "Acme",
    title: "Acme Spot",
    brief: null,
    script: null,
    source: "owner",
    packSlug: null,
    spotSeconds: 30,
    voicePlan: null,
    bedMediaId: null,
    state: "draft",
    failReason: null,
    mediaId: null,
    createdAt: "2026-09-01T00:00:00Z",
    stateChangedAt: "2026-09-01T00:00:00Z",
    renderedAt: null,
    retiredAt: null,
    version: "100",
    ...overrides,
  };
}

function adBrief(overrides: Partial<AdBriefDto> = {}): AdBriefDto {
  return {
    id: 1,
    packSlug: null,
    brand: "Acme",
    premise: "A premise",
    tone: null,
    structure: null,
    enabled: true,
    createdAt: "2026-09-01T00:00:00Z",
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// Fetch mock — a small route table (method + pathname predicate → response), generalizing
// gardener-tabs.spec.tsx's own inline if-chain since this page's surface spans many more distinct
// routes (create/edit/approve/retry/retire, voices, two briefs endpoints).
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

interface RouteHandler {
  method: string;
  match: (url: URL) => boolean;
  respond: (url: URL, init: RequestInit | undefined) => RouteResponseSpec;
}

function toResponse(spec: RouteResponseSpec): Response {
  return {
    ok: spec.status >= 200 && spec.status < 300,
    status: spec.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

/** `apiGet` (page.tsx's own reads) always hands an absolute BACKEND_URL-prefixed request; every
 * browser-side ads-api.ts fetcher hands a bare relative path instead — a base origin lets `URL()`
 * parse both the same way real `fetch()` resolution would (the gardener-tabs.spec.tsx precedent). */
function installFetchMock(handlers: RouteHandler[]): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = new URL(String(input), "http://localhost");
    const handler = handlers.find((h) => h.method === method && h.match(url));
    if (handler === undefined) {
      throw new Error(`unexpected fetch call: ${method} ${url.pathname}${url.search}`);
    }
    return toResponse(handler.respond(url, init));
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function requestBody(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number): unknown {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit];
  return JSON.parse(String(call[1].body));
}

function requestHeader(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number, name: string): string | null {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit];
  const headers = call[1]?.headers as Record<string, string> | undefined;
  return headers?.[name] ?? null;
}

function findCallIndex(
  mockFetch: jest.MockedFunction<typeof fetch>,
  method: string,
  urlPredicate: (url: string) => boolean
): number {
  return mockFetch.mock.calls.findIndex(
    ([url, init]) => urlPredicate(String(url)) && ((init as RequestInit | undefined)?.method ?? "GET") === method
  );
}

async function renderAdsPage(sp: Record<string, string>): Promise<ReturnType<typeof render>> {
  const { default: AdsPage } = await import("../app/(authed)/ads/page");
  const node = await AdsPage({ searchParams: Promise.resolve(sp) });
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

beforeEach(() => {
  mockedRefresh.mockClear();
  mockedUseRouter.mockReturnValue({ refresh: mockedRefresh } as unknown as ReturnType<typeof useRouter>);
});

afterEach(() => {
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------

describe("Feature: The Ads page", () => {
  describe("Scenario: spots list by state", () => {
    it("requests only the active tab's own state via ?state=, never a cross-tab fan-out", async () => {
      const mockFetch = installFetchMock([
        {
          method: "GET",
          match: (u) => u.pathname === "/api/ads",
          respond: () => ({ status: 200, body: { items: [adSpot({ state: "ready", title: "Ready Spot" })], total: 1 } }),
        },
      ]);

      await renderAdsPage({ tab: "ready" });

      const calls = mockFetch.mock.calls.filter(([input]) => new URL(String(input), "http://localhost").pathname === "/api/ads");
      expect(calls).toHaveLength(1);
      const url = new URL(String(calls[0]?.[0]), "http://localhost");
      expect(url.searchParams.get("state")).toBe("ready");
      expect(screen.getByText("Ready Spot")).toBeInTheDocument();
    });

    it("shows the active tab's own total, leaving every tab unbadged (no per-tab fan-out)", () => {
      render(<AdsTabs activeTab="draft" limit={DEFAULT_ADS_PAGE_SIZE} />);
      const nav = screen.getByRole("navigation", { name: "Ads sections" });
      for (const link of within(nav).getAllByRole("link")) {
        expect(link.textContent).not.toMatch(/\d/);
      }

      render(
        <ConfirmDialogProvider>
          <AdsSection tab="draft" items={[]} total={7} />
        </ConfirmDialogProvider>
      );
      const section = screen.getByRole("region", { name: "Draft" });
      expect(within(section).getByText("7 total", { exact: false })).toBeInTheDocument();
    });

    it("pages on the shared pager with the 50-default size picker", () => {
      render(
        <PageSizePicker sizes={ADS_PAGE_SIZES} limit={DEFAULT_ADS_PAGE_SIZE} hrefFor={(size) => buildAdsHref("draft", size)} />
      );

      const group = screen.getByRole("group", { name: "Rows per page" });
      expect(within(group).getByRole("link", { name: "50" })).toHaveAttribute("aria-current", "page");
      expect(within(group).getByRole("link", { name: "50" })).toHaveAttribute("href", "/ads");
      expect(within(group).getByRole("link", { name: "25" })).not.toHaveAttribute("aria-current");
      expect(within(group).getByRole("link", { name: "200" })).toHaveAttribute("href", "/ads?limit=200");
    });
  });

  describe("Scenario: verb gating across all six states (F2)", () => {
    // The full `AdSpotRow` transition matrix, table-driven — every state gets its own row, exactly
    // once, and every verb is asserted both present (when legal) and ABSENT (queryByRole, when
    // not) rather than only checking the positive case. Mirrors `AdsController`'s own guards
    // exactly: approve is draft-only, retry is failed-only, edit is draft/failed (PATCH's own
    // legal-from set), retire is ready|draft|approved|failed (never rendering/retired), preview is
    // ready-only.
    interface GatingExpectation {
      state: AdState;
      edit: boolean;
      approve: boolean;
      retry: boolean;
      retire: boolean;
      preview: boolean;
    }

    const GATING_TABLE: readonly GatingExpectation[] = [
      { state: "draft", edit: true, approve: true, retry: false, retire: true, preview: false },
      { state: "approved", edit: false, approve: false, retry: false, retire: true, preview: false },
      { state: "rendering", edit: false, approve: false, retry: false, retire: false, preview: false },
      { state: "ready", edit: false, approve: false, retry: false, retire: true, preview: true },
      { state: "failed", edit: true, approve: false, retry: true, retire: true, preview: false },
      { state: "retired", edit: false, approve: false, retry: false, retire: false, preview: false },
    ];

    function expectVerb(row: HTMLElement, name: string, present: boolean): void {
      if (present) {
        expect(within(row).getByRole("button", { name })).toBeInTheDocument();
      } else {
        expect(within(row).queryByRole("button", { name })).not.toBeInTheDocument();
      }
    }

    it.each(GATING_TABLE)(
      "renders exactly the legal verbs for state=$state",
      ({ state, edit, approve, retry, retire, preview }) => {
        const spot = adSpot({ id: 9, state, mediaId: state === "ready" ? 999 : null });

        render(
          <ConfirmDialogProvider>
            <AdsSection tab={state} items={[spot]} total={1} />
          </ConfirmDialogProvider>
        );

        const row = screen.getByText(spot.title).closest("div.py-3") as HTMLElement;

        expectVerb(row, "Edit", edit);
        expectVerb(row, "Approve", approve);
        expectVerb(row, "Retry", retry);
        expectVerb(row, "Retire", retire);
        expectVerb(row, "Preview", preview);
      }
    );
  });

  describe("Scenario: the editor round-trips", () => {
    it("saves a valid draft and re-opens it with every field intact", async () => {
      const createdDto = adSpot({
        id: 42,
        brand: "Acme Radio",
        title: "Acme Radio Spot",
        brief: "A warm brief",
        script: "A single line about the sale.",
        spotSeconds: 60,
        bedMediaId: 777,
        voicePlan: null,
        version: "200",
      });

      installFetchMock([{ method: "POST", match: (u) => u.pathname === "/api/ads", respond: () => ({ status: 201, body: createdDto }) }]);

      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={null} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Brand"), { target: { value: "Acme Radio" } });
      fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Acme Radio Spot" } });
      fireEvent.change(screen.getByLabelText("Brief"), { target: { value: "A warm brief" } });
      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "A single line about the sale." } });
      fireEvent.change(screen.getByLabelText("Length"), { target: { value: "60" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(onSaved).toHaveBeenCalledWith(createdDto));

      // Re-open: a fresh AdSpotEditor mount seeded with the row just saved — every field must
      // round-trip. `cleanup()` first: Radix portals its Dialog.Content into `document.body`, not
      // into the RTL container, so the first dialog can't be scoped away with `within()`.
      cleanup();
      render(<AdSpotEditor initial={createdDto} onSaved={jest.fn()} onCancel={jest.fn()} />);

      expect(screen.getByLabelText("Brand")).toHaveValue("Acme Radio");
      expect(screen.getByLabelText("Title")).toHaveValue("Acme Radio Spot");
      expect(screen.getByLabelText("Brief")).toHaveValue("A warm brief");
      expect(screen.getByLabelText("Script")).toHaveValue("A single line about the sale.");
      expect(screen.getByLabelText("Length")).toHaveValue("60");
      expect(screen.getByText("#777")).toBeInTheDocument();
    });

    it("offers voices from GET /api/voices and beds via the BedPicker", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/voices", respond: () => ({ status: 200, body: ["voice-a", "voice-b"] }) },
      ]);

      render(<AdSpotEditor initial={null} onSaved={jest.fn()} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "ANNOUNCER: Hello there." } });

      expect(await screen.findByText("ANNOUNCER")).toBeInTheDocument();
      const voiceSelect = screen.getByLabelText("Voice");
      await waitFor(() => expect(within(voiceSelect).getByRole("option", { name: "voice-a" })).toBeInTheDocument());
      expect(within(voiceSelect).getByRole("option", { name: "voice-b" })).toBeInTheDocument();

      expect(screen.getByLabelText("Bed (optional)")).toBeInTheDocument();
      expect(screen.getByRole("button", { name: "Search" })).toBeInTheDocument();
    });

    it("parses a tag whose colon has surrounding whitespace, matching the server's split-first-colon-then-trim shape (fold h)", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/voices", respond: () => ({ status: 200, body: ["voice-a"] }) },
      ]);

      render(<AdSpotEditor initial={null} onSaved={jest.fn()} onCancel={jest.fn()} />);

      // A space before the colon — AdScriptParser.ParseLine splits at the FIRST ':' then trims
      // both sides, so the server accepts this tag exactly like "ANNOUNCER:" with no space. The
      // old client-side regex (`/^([A-Z0-9]+):/`) required the colon flush against the tag and
      // silently failed to offer a picker here — this pins the fix.
      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "ANNOUNCER : Hello there." } });

      expect(await screen.findByText("ANNOUNCER")).toBeInTheDocument();
    });
  });

  describe("Scenario: editing an existing spot — the PATCH path (F3)", () => {
    it('PATCHes /api/ads/{id} with If-Match: W/"<initial.version>" on save', async () => {
      const initialSpot = adSpot({ id: 7, state: "draft", version: "555", brand: "Old Brand" });
      const updatedSpot = { ...initialSpot, brand: "New Brand", version: "556" };

      const mockFetch = installFetchMock([
        { method: "PATCH", match: (u) => u.pathname === "/api/ads/7", respond: () => ({ status: 200, body: updatedSpot }) },
      ]);

      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={initialSpot} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Brand"), { target: { value: "New Brand" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(onSaved).toHaveBeenCalledWith(updatedSpot));
      const callIndex = findCallIndex(mockFetch, "PATCH", (u) => u === "/api/ads/7");
      expect(callIndex).toBeGreaterThan(-1);
      expect(requestHeader(mockFetch, callIndex, "If-Match")).toBe('W/"555"');
    });

    it("surfaces a stale-version 409 without calling onSaved", async () => {
      const initialSpot = adSpot({ id: 8, state: "draft", version: "600" });

      installFetchMock([
        {
          method: "PATCH",
          match: (u) => u.pathname === "/api/ads/8",
          respond: () => ({
            status: 409,
            body: {
              title: "Conflict.",
              detail:
                "The spot was modified since you last read it, or is no longer in a state this action allows. Re-fetch and retry.",
            },
          }),
        },
      ]);

      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={initialSpot} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Retitled" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(/modified since you last read it/)).toBeInTheDocument();
      expect(onSaved).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: honest clears on edit (F1 — the sparse-PATCH can't-clear gap)", () => {
    it("hides the bed's Clear affordance while editing a spot that already has one", () => {
      const initialSpot = adSpot({ id: 11, state: "draft", bedMediaId: 42 });

      render(<AdSpotEditor initial={initialSpot} onSaved={jest.fn()} onCancel={jest.fn()} />);

      expect(screen.getByText("#42")).toBeInTheDocument();
      expect(screen.queryByRole("button", { name: "Clear" })).not.toBeInTheDocument();
    });

    it("still shows Clear for a bed picked fresh during create (no committed row to silently fail to clear)", async () => {
      installFetchMock([
        {
          method: "GET",
          match: (u) => u.pathname === "/api/media",
          respond: () => ({ status: 200, body: [{ mediaId: "9", title: "Jingle", artist: null }] }),
        },
      ]);

      render(<AdSpotEditor initial={null} onSaved={jest.fn()} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Bed (optional)"), { target: { value: "jingle" } });
      fireEvent.click(screen.getByRole("button", { name: "Search" }));
      fireEvent.click(await screen.findByRole("button", { name: "Select" }));

      expect(screen.getByRole("button", { name: "Clear" })).toBeInTheDocument();
    });

    it("refuses to submit a previously-set script emptied to blank, naming the limitation", async () => {
      // No route ever expected — the client-side guard must block the request entirely.
      installFetchMock([]);

      const initialSpot = adSpot({ id: 12, state: "draft", script: "ANNOUNCER: Keep this." });
      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={initialSpot} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "   " } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(/can't be cleared once set/)).toBeInTheDocument();
      expect(onSaved).not.toHaveBeenCalled();
    });

    it("refuses to submit a previously-set brief emptied to blank, naming the limitation", async () => {
      installFetchMock([]);

      const initialSpot = adSpot({ id: 14, state: "draft", brief: "Keep this brief." });
      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={initialSpot} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Brief"), { target: { value: "" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(/can't be cleared once set/)).toBeInTheDocument();
      expect(onSaved).not.toHaveBeenCalled();
    });

    it("pins an already-cast tag's voice — reverting to Station default doesn't stick", async () => {
      installFetchMock([
        { method: "GET", match: (u) => u.pathname === "/api/voices", respond: () => ({ status: 200, body: ["voice-a", "voice-b"] }) },
      ]);

      const initialSpot = adSpot({
        id: 13,
        state: "draft",
        script: "ANNOUNCER: Keep casting.",
        voicePlan: [{ tag: "ANNOUNCER", voiceId: "voice-a", pace: 1.0 }],
      });

      render(<AdSpotEditor initial={initialSpot} onSaved={jest.fn()} onCancel={jest.fn()} />);

      const voiceSelect = (await screen.findByLabelText("Voice")) as HTMLSelectElement;
      await waitFor(() => expect(voiceSelect.value).toBe("voice-a"));

      fireEvent.change(voiceSelect, { target: { value: "" } });

      expect(voiceSelect.value).toBe("voice-a");
      expect(screen.getByText(/Already cast/)).toBeInTheDocument();
    });
  });

  describe("Scenario: verbs drive the state machine", () => {
    it("approve/retry/retire move the row and refresh from server truth", async () => {
      const draftSpot = adSpot({ id: 1, state: "draft", version: "10" });
      const failedSpot = adSpot({ id: 2, state: "failed", version: "20", failReason: "duration" });
      const readySpot = adSpot({ id: 3, state: "ready", version: "30", mediaId: 999 });

      const mockFetch = installFetchMock([
        {
          method: "POST",
          match: (u) => u.pathname === "/api/ads/1/approve",
          respond: () => ({ status: 200, body: { ...draftSpot, state: "approved", version: "11" } }),
        },
        {
          method: "POST",
          match: (u) => u.pathname === "/api/ads/2/retry",
          respond: () => ({ status: 200, body: { ...failedSpot, state: "approved", version: "21" } }),
        },
        {
          method: "POST",
          match: (u) => u.pathname === "/api/ads/3/retire",
          respond: () => ({ status: 200, body: { ...readySpot, state: "retired", version: "31" } }),
        },
      ]);

      render(
        <ConfirmDialogProvider>
          <AdsSection tab="draft" items={[draftSpot, failedSpot, readySpot]} total={3} />
          <Toaster />
        </ConfirmDialogProvider>
      );

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Approve" }));
        await Promise.resolve();
      });
      await waitFor(() => expect(findCallIndex(mockFetch, "POST", (u) => u === "/api/ads/1/approve")).toBeGreaterThan(-1));
      expect(requestHeader(mockFetch, findCallIndex(mockFetch, "POST", (u) => u === "/api/ads/1/approve"), "If-Match")).toBe(
        'W/"10"'
      );

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Retry" }));
        await Promise.resolve();
      });
      await waitFor(() => expect(findCallIndex(mockFetch, "POST", (u) => u === "/api/ads/2/retry")).toBeGreaterThan(-1));

      // Every row here can Retire (draft/failed/ready all can) — scope to the ready row via its
      // own Preview button, the one verb unique to that state, rather than an ambiguous bare query.
      const readyRow = screen.getByRole("button", { name: "Preview" }).closest("div.py-3") as HTMLElement;
      await act(async () => {
        fireEvent.click(within(readyRow).getByRole("button", { name: "Retire" }));
        await Promise.resolve();
      });
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Retire" }));
        await Promise.resolve();
      });
      await waitFor(() => expect(findCallIndex(mockFetch, "POST", (u) => u === "/api/ads/3/retire")).toBeGreaterThan(-1));

      await waitFor(() => expect(mockedRefresh).toHaveBeenCalledTimes(3));
    });
  });

  describe("Scenario: ready spots preview", () => {
    it("plays the rendered artifact in the browser", () => {
      const readySpot = adSpot({ id: 5, state: "ready", mediaId: 555, spotSeconds: 30, version: "50" });

      const { container } = render(
        <ConfirmDialogProvider>
          <AdsSection tab="ready" items={[readySpot]} total={1} />
        </ConfirmDialogProvider>
      );

      // No <audio> element before Preview is clicked — the reveal interaction still gates it.
      expect(container.querySelector("audio")).toBeNull();

      fireEvent.click(screen.getByRole("button", { name: "Preview" }));

      const audio = container.querySelector("audio");
      expect(audio).toBeInTheDocument();
      expect(audio).toHaveAttribute("src", "/api/media/555/audio");
      expect(audio).toHaveAttribute("controls");
      expect(audio).toHaveAttribute("preload", "none");
    });
  });

  describe("Scenario: briefs are manageable", () => {
    it("lists pack and owner briefs with enable/disable toggles", async () => {
      const packBrief = adBrief({ id: 10, brand: "PackCo", packSlug: "brand-pack", enabled: true, premise: "From the pack" });
      const ownerBrief = adBrief({ id: 11, brand: "OwnerCo", packSlug: null, enabled: false, premise: "My own brief" });

      const mockFetch = installFetchMock([
        {
          method: "PATCH",
          match: (u) => u.pathname === "/api/ad-briefs/10",
          respond: () => ({ status: 200, body: { ...packBrief, enabled: false } }),
        },
      ]);

      render(
        <ConfirmDialogProvider>
          <BriefsSection briefs={[packBrief, ownerBrief]} />
        </ConfirmDialogProvider>
      );

      expect(screen.getByText("PackCo")).toBeInTheDocument();
      expect(screen.getByText("Pack: brand-pack")).toBeInTheDocument();
      expect(screen.getByText("OwnerCo")).toBeInTheDocument();
      expect(screen.getByText("Owner")).toBeInTheDocument();

      const packToggle = screen.getByRole("checkbox", { name: "Enabled: PackCo" });
      expect(packToggle).toBeChecked();
      const ownerToggle = screen.getByRole("checkbox", { name: "Enabled: OwnerCo" });
      expect(ownerToggle).not.toBeChecked();

      await act(async () => {
        fireEvent.click(packToggle);
        await Promise.resolve();
      });

      await waitFor(() => expect(findCallIndex(mockFetch, "PATCH", (u) => u === "/api/ad-briefs/10")).toBeGreaterThan(-1));
      const callIndex = findCallIndex(mockFetch, "PATCH", (u) => u === "/api/ad-briefs/10");
      expect(requestBody(mockFetch, callIndex)).toEqual({ enabled: false });
      await waitFor(() => expect(mockedRefresh).toHaveBeenCalled());
    });

    it("adds an owner brief through the form", async () => {
      const mockFetch = installFetchMock([
        {
          method: "POST",
          match: (u) => u.pathname === "/api/ad-briefs",
          respond: () => ({ status: 201, body: adBrief({ id: 20, brand: "NewBrand", packSlug: null }) }),
        },
      ]);

      render(
        <ConfirmDialogProvider>
          <BriefsSection briefs={[]} />
        </ConfirmDialogProvider>
      );

      fireEvent.change(screen.getByLabelText("Brand"), { target: { value: "NewBrand" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Add brief" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(findCallIndex(mockFetch, "POST", (u) => u === "/api/ad-briefs")).toBeGreaterThan(-1));
      const callIndex = findCallIndex(mockFetch, "POST", (u) => u === "/api/ad-briefs");
      expect(requestBody(mockFetch, callIndex)).toEqual({ brand: "NewBrand", premise: null, tone: null, structure: null });
      await waitFor(() => expect(mockedRefresh).toHaveBeenCalled());
    });

    it("shows the server's own 409 duplicate-brand message inline (fold g)", async () => {
      installFetchMock([
        {
          method: "POST",
          match: (u) => u.pathname === "/api/ad-briefs",
          respond: () => ({
            status: 409,
            body: {
              title: "Conflict.",
              detail:
                'An owner-authored brief for brand "OwnerCo" already exists — edit it instead of creating a second one.',
              field: "brand",
            },
          }),
        },
      ]);

      render(
        <ConfirmDialogProvider>
          <BriefsSection briefs={[]} />
        </ConfirmDialogProvider>
      );

      fireEvent.change(screen.getByLabelText("Brand"), { target: { value: "OwnerCo" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Add brief" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(/already exists — edit it instead/)).toBeInTheDocument();
    });
  });

  describe("Scenario: rejecting invalid input", () => {
    it("surfaces the validator's 400 rule id on the offending field", async () => {
      installFetchMock([
        {
          method: "POST",
          match: (u) => u.pathname === "/api/ads",
          respond: () => ({
            status: 400,
            body: { detail: "script the estimated read time exceeds the spot's 30s length.", field: "script", ruleId: "duration" },
          }),
        },
      ]);

      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={null} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Brand"), { target: { value: "Acme" } });
      fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Acme Spot" } });
      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "way too long a script to fit" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(/rule: duration/)).toBeInTheDocument();
      expect(screen.getByText(/exceeds the spot's 30s length/)).toBeInTheDocument();
      expect(onSaved).not.toHaveBeenCalled();
    });
  });
});
