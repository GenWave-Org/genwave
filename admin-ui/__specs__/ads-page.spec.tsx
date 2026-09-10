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
import type { AdBriefDto, AdSpotDto, AdState, AdVoicePlanEntry } from "@/lib/ads-api";
import type { SponsorListItemDto, SponsorRefDto } from "@/lib/sponsors-api";
import { ADS_PAGE_SIZES, buildAdsHref, buildAdsPageHref, DEFAULT_ADS_PAGE_SIZE } from "../app/(authed)/ads/ads-paging";
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

/** Two sponsors, distinct ids/names — a create-mode test picks between them via the "Sponsor"
 * select rather than typing free text (PLAN T447's replacement of the old Brand input). */
const SPONSOR_ACME: SponsorRefDto = { id: 1, name: "Acme", paused: false };
const SPONSOR_ACME_RADIO: SponsorRefDto = { id: 2, name: "Acme Radio", paused: false };
const SPONSOR_REFS: readonly SponsorRefDto[] = [SPONSOR_ACME, SPONSOR_ACME_RADIO];

function sponsorListItem(overrides: Partial<SponsorListItemDto> = {}): SponsorListItemDto {
  return {
    id: 1,
    name: "Acme",
    packSlug: null,
    paused: false,
    pausedAt: null,
    tagline: null,
    about: null,
    phone: null,
    address: null,
    website: null,
    tone: null,
    createdAt: "2026-09-01T00:00:00Z",
    updatedAt: "2026-09-01T00:00:00Z",
    briefs: 0,
    spots: {},
    shows: 0,
    ...overrides,
  };
}

/** `GET /api/sponsors`'s own response (`page.tsx`'s required fetch alongside every tab's own data)
 * — every `renderAdsPage()` call needs a route for it, or the mock throws "unexpected fetch call". */
const SPONSORS_RESPONSE: SponsorListItemDto[] = [
  sponsorListItem({ id: 1, name: "Acme" }),
  sponsorListItem({ id: 2, name: "Acme Radio" }),
];

function adSpot(overrides: Partial<AdSpotDto> = {}): AdSpotDto {
  return {
    id: 1,
    sponsorId: 1,
    sponsorName: "Acme",
    sponsor: SPONSOR_ACME,
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
    sponsor: SPONSOR_ACME,
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
          match: (u) => u.pathname === "/api/sponsors",
          respond: () => ({ status: 200, body: SPONSORS_RESPONSE }),
        },
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
      render(<AdsTabs activeTab="draft" limit={DEFAULT_ADS_PAGE_SIZE} sponsorId={null} />);
      const nav = screen.getByRole("navigation", { name: "Ads sections" });
      for (const link of within(nav).getAllByRole("link")) {
        expect(link.textContent).not.toMatch(/\d/);
      }

      render(
        <ConfirmDialogProvider>
          <AdsSection tab="draft" items={[]} total={7} sponsorId={null} sponsors={SPONSOR_REFS} />
        </ConfirmDialogProvider>
      );
      const section = screen.getByRole("region", { name: "Draft" });
      expect(within(section).getByText("7 total", { exact: false })).toBeInTheDocument();
    });

    it("pages on the shared pager with the 50-default size picker", () => {
      render(
        <PageSizePicker
          sizes={ADS_PAGE_SIZES}
          limit={DEFAULT_ADS_PAGE_SIZE}
          hrefFor={(size) => buildAdsHref("draft", size, null)}
        />
      );

      const group = screen.getByRole("group", { name: "Rows per page" });
      expect(within(group).getByRole("link", { name: "50" })).toHaveAttribute("aria-current", "page");
      expect(within(group).getByRole("link", { name: "50" })).toHaveAttribute("href", "/ads");
      expect(within(group).getByRole("link", { name: "25" })).not.toHaveAttribute("aria-current");
      expect(within(group).getByRole("link", { name: "200" })).toHaveAttribute("href", "/ads?limit=200");
    });

    it("threads the rail's own sponsor selection through every tab link's href", () => {
      render(<AdsTabs activeTab="draft" limit={DEFAULT_ADS_PAGE_SIZE} sponsorId={7} />);
      const nav = screen.getByRole("navigation", { name: "Ads sections" });
      for (const link of within(nav).getAllByRole("link")) {
        expect(link.getAttribute("href")).toContain("sponsor=7");
      }
    });

    it("builds the pager's and the size picker's own hrefs with the sponsor id included, exact string", () => {
      expect(buildAdsPageHref("ready", 50, 7, 2)).toBe("/ads?tab=ready&sponsor=7&page=2");
      expect(buildAdsHref("ready", 200, 7)).toBe("/ads?tab=ready&limit=200&sponsor=7");
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
            <AdsSection tab={state} items={[spot]} total={1} sponsorId={null} sponsors={SPONSOR_REFS} />
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

  describe("Scenario: read-only voice-cast chips on a spot row (F167.5)", () => {
    // Table-driven, mirroring the verb-gating scenario above: chips render iff the state is one of
    // the three the two documents name between them (F167.5: `ready`/`rendering`; PLAN :1524's
    // acceptance line: `approved`) AND the plan is a non-empty array — `null` and `[]` both count
    // as "no plan" (PLAN T420).
    const CAST_PLAN: AdVoicePlanEntry[] = [
      { tag: "ANNOUNCER", voiceId: "af_heart", pace: 1 },
      { tag: "VOICE1", voiceId: "am_michael", pace: 1 },
      { tag: "VOICE2", voiceId: "af_bella", pace: 1 },
    ];

    interface CastExpectation {
      name: string;
      state: AdState;
      voicePlan: AdVoicePlanEntry[] | null;
      expectChips: boolean;
    }

    const CAST_TABLE: readonly CastExpectation[] = [
      { name: "ready + 3-entry plan renders three chips", state: "ready", voicePlan: CAST_PLAN, expectChips: true },
      { name: "rendering + plan renders chips", state: "rendering", voicePlan: CAST_PLAN, expectChips: true },
      { name: "approved + plan renders chips", state: "approved", voicePlan: CAST_PLAN, expectChips: true },
      { name: "draft + plan renders no group", state: "draft", voicePlan: CAST_PLAN, expectChips: false },
      { name: "ready + null plan renders no group", state: "ready", voicePlan: null, expectChips: false },
      { name: "ready + empty plan renders no group", state: "ready", voicePlan: [], expectChips: false },
      { name: "failed + plan renders no group", state: "failed", voicePlan: CAST_PLAN, expectChips: false },
      { name: "retired + plan renders no group", state: "retired", voicePlan: CAST_PLAN, expectChips: false },
    ];

    it.each(CAST_TABLE)("$name", ({ state, voicePlan, expectChips }) => {
      const spot = adSpot({ id: 9, state, voicePlan, mediaId: state === "ready" ? 999 : null });

      render(
        <ConfirmDialogProvider>
          <AdsSection tab={state} items={[spot]} total={1} sponsorId={null} sponsors={SPONSOR_REFS} />
        </ConfirmDialogProvider>
      );

      const row = screen.getByText(spot.title).closest("div.py-3") as HTMLElement;

      if (!expectChips) {
        expect(within(row).queryByRole("group", { name: "Voice cast" })).not.toBeInTheDocument();
        return;
      }

      const group = within(row).getByRole("group", { name: "Voice cast" });
      const chipTexts = Array.from(group.children).map((chip) => chip.textContent ?? "");
      expect(chipTexts).toEqual(["ANNOUNCER · af_heart", "VOICE1 · am_michael", "VOICE2 · af_bella"]);
    });

    it("still renders the source chip beside the cast (regression pin)", () => {
      const spot = adSpot({ id: 9, state: "ready", source: "pack", voicePlan: CAST_PLAN, mediaId: 999 });

      render(
        <ConfirmDialogProvider>
          <AdsSection tab="ready" items={[spot]} total={1} sponsorId={null} sponsors={SPONSOR_REFS} />
        </ConfirmDialogProvider>
      );

      const row = screen.getByText(spot.title).closest("div.py-3") as HTMLElement;
      expect(within(row).getByText("Pack")).toBeInTheDocument();
      expect(within(row).getByRole("group", { name: "Voice cast" })).toBeInTheDocument();
    });
  });

  describe("Scenario: the editor round-trips", () => {
    it("saves a valid draft and re-opens it with every field intact", async () => {
      const createdDto = adSpot({
        id: 42,
        sponsorId: 2,
        sponsorName: "Acme Radio",
        sponsor: SPONSOR_ACME_RADIO,
        title: "Acme Radio Spot",
        brief: "A warm brief",
        script: "A single line about the sale.",
        spotSeconds: 60,
        bedMediaId: 777,
        voicePlan: null,
        version: "200",
      });

      const mockFetch = installFetchMock([
        { method: "POST", match: (u) => u.pathname === "/api/ads", respond: () => ({ status: 201, body: createdDto }) },
      ]);

      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={null} sponsors={SPONSOR_REFS} onSaved={onSaved} onCancel={jest.fn()} />);

      expect(screen.getByLabelText("Sponsor")).toHaveValue("");

      fireEvent.change(screen.getByLabelText("Sponsor"), { target: { value: "2" } });
      fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Acme Radio Spot" } });
      fireEvent.change(screen.getByLabelText("Brief"), { target: { value: "A warm brief" } });
      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "A single line about the sale." } });
      fireEvent.change(screen.getByLabelText("Length"), { target: { value: "60" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(onSaved).toHaveBeenCalledWith(createdDto));

      // PLAN T447 ruling: the POST body carries the chosen sponsor by id, on the wire's own
      // `sponsorId` field — never a free-text company name (gh-#707's own reading extends to the
      // wire shape, not just the rendered page: no "brand" key survives the old free-text field).
      const postedBody = requestBody(mockFetch, 0) as Record<string, unknown>;
      expect(postedBody).toMatchObject({ sponsorId: 2 });
      expect(Object.keys(postedBody)).not.toContain("brand");

      // Re-open: a fresh AdSpotEditor mount seeded with the row just saved — every field must
      // round-trip. `cleanup()` first: Radix portals its Dialog.Content into `document.body`, not
      // into the RTL container, so the first dialog can't be scoped away with `within()`.
      cleanup();
      render(<AdSpotEditor initial={createdDto} sponsors={SPONSOR_REFS} onSaved={jest.fn()} onCancel={jest.fn()} />);

      expect(screen.getByLabelText("Sponsor")).toHaveValue("2");
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

      render(<AdSpotEditor initial={null} sponsors={SPONSOR_REFS} onSaved={jest.fn()} onCancel={jest.fn()} />);

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

      render(<AdSpotEditor initial={null} sponsors={SPONSOR_REFS} onSaved={jest.fn()} onCancel={jest.fn()} />);

      // A space before the colon — AdScriptParser.ParseLine splits at the FIRST ':' then trims
      // both sides, so the server accepts this tag exactly like "ANNOUNCER:" with no space. The
      // old client-side regex (`/^([A-Z0-9]+):/`) required the colon flush against the tag and
      // silently failed to offer a picker here — this pins the fix.
      fireEvent.change(screen.getByLabelText("Script"), { target: { value: "ANNOUNCER : Hello there." } });

      expect(await screen.findByText("ANNOUNCER")).toBeInTheDocument();
    });

    it("flags a paused sponsor in its own picker option, suffixed onto the name", () => {
      const sponsorsWithPaused: readonly SponsorRefDto[] = [
        SPONSOR_ACME,
        { id: 3, name: "Riverside Diner", paused: true },
      ];

      render(<AdSpotEditor initial={null} sponsors={sponsorsWithPaused} onSaved={jest.fn()} onCancel={jest.fn()} />);

      const sponsorSelect = screen.getByLabelText("Sponsor");
      expect(within(sponsorSelect).getByRole("option", { name: "Riverside Diner (paused)" })).toBeInTheDocument();
      expect(within(sponsorSelect).getByRole("option", { name: "Acme" })).toBeInTheDocument();
    });
  });

  describe("Scenario: editing an existing spot — the PATCH path (F3)", () => {
    it('PATCHes /api/ads/{id} with If-Match: W/"<initial.version>" on save', async () => {
      const initialSpot = adSpot({ id: 7, state: "draft", version: "555" });
      const updatedSpot = { ...initialSpot, sponsorId: 2, sponsorName: "Acme Radio", sponsor: SPONSOR_ACME_RADIO, version: "556" };

      const mockFetch = installFetchMock([
        { method: "PATCH", match: (u) => u.pathname === "/api/ads/7", respond: () => ({ status: 200, body: updatedSpot }) },
      ]);

      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={initialSpot} sponsors={SPONSOR_REFS} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Sponsor"), { target: { value: "2" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(onSaved).toHaveBeenCalledWith(updatedSpot));
      const callIndex = findCallIndex(mockFetch, "PATCH", (u) => u === "/api/ads/7");
      expect(callIndex).toBeGreaterThan(-1);
      expect(requestHeader(mockFetch, callIndex, "If-Match")).toBe('W/"555"');

      // PLAN T447 ruling: same wire shape as the create path — the PATCH body also carries the
      // sponsor by id, never a free-text name.
      const patchedBody = requestBody(mockFetch, callIndex) as Record<string, unknown>;
      expect(patchedBody).toMatchObject({ sponsorId: 2 });
      expect(Object.keys(patchedBody)).not.toContain("brand");
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
      render(<AdSpotEditor initial={initialSpot} sponsors={SPONSOR_REFS} onSaved={onSaved} onCancel={jest.fn()} />);

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

      render(<AdSpotEditor initial={initialSpot} sponsors={SPONSOR_REFS} onSaved={jest.fn()} onCancel={jest.fn()} />);

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

      render(<AdSpotEditor initial={null} sponsors={SPONSOR_REFS} onSaved={jest.fn()} onCancel={jest.fn()} />);

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
      render(<AdSpotEditor initial={initialSpot} sponsors={SPONSOR_REFS} onSaved={onSaved} onCancel={jest.fn()} />);

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
      render(<AdSpotEditor initial={initialSpot} sponsors={SPONSOR_REFS} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Brief"), { target: { value: "" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      expect(await screen.findByText(/can't be cleared once set/)).toBeInTheDocument();
      expect(onSaved).not.toHaveBeenCalled();
    });

    it("refuses to submit with no sponsor chosen, in plain wording, without ever calling the api", async () => {
      const mockFetch = installFetchMock([]);

      const onSaved = jest.fn<(spot: AdSpotDto) => void>();
      render(<AdSpotEditor initial={null} sponsors={SPONSOR_REFS} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Title"), { target: { value: "Untitled" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Save" }));
        await Promise.resolve();
      });

      expect(await screen.findByRole("alert")).toHaveTextContent("Sponsor is required.");
      expect(onSaved).not.toHaveBeenCalled();
      expect(mockFetch).not.toHaveBeenCalled();
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

      render(<AdSpotEditor initial={initialSpot} sponsors={SPONSOR_REFS} onSaved={jest.fn()} onCancel={jest.fn()} />);

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
          <AdsSection
            tab="draft"
            items={[draftSpot, failedSpot, readySpot]}
            total={3}
            sponsorId={null}
            sponsors={SPONSOR_REFS}
          />
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
          <AdsSection tab="ready" items={[readySpot]} total={1} sponsorId={null} sponsors={SPONSOR_REFS} />
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
      const packBrief = adBrief({
        id: 10,
        sponsor: { id: 3, name: "PackCo", paused: false },
        packSlug: "brand-pack",
        enabled: true,
        premise: "From the pack",
      });
      const ownerBrief = adBrief({
        id: 11,
        sponsor: { id: 4, name: "OwnerCo", paused: false },
        packSlug: null,
        enabled: false,
        premise: "My own brief",
      });

      const mockFetch = installFetchMock([
        {
          method: "PATCH",
          match: (u) => u.pathname === "/api/ad-briefs/10",
          respond: () => ({ status: 200, body: { ...packBrief, enabled: false } }),
        },
      ]);

      render(
        <ConfirmDialogProvider>
          <BriefsSection briefs={[packBrief, ownerBrief]} sponsorId={null} sponsors={SPONSOR_REFS} />
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
          respond: () => ({ status: 201, body: adBrief({ id: 20, sponsor: SPONSOR_ACME_RADIO, packSlug: null }) }),
        },
      ]);

      render(
        <ConfirmDialogProvider>
          <BriefsSection briefs={[]} sponsorId={null} sponsors={SPONSOR_REFS} />
        </ConfirmDialogProvider>
      );

      fireEvent.change(screen.getByLabelText("Sponsor"), { target: { value: "2" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Add brief" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(findCallIndex(mockFetch, "POST", (u) => u === "/api/ad-briefs")).toBeGreaterThan(-1));
      const callIndex = findCallIndex(mockFetch, "POST", (u) => u === "/api/ad-briefs");
      expect(requestBody(mockFetch, callIndex)).toEqual({ sponsorId: 2, premise: null, tone: null, structure: null });
      await waitFor(() => expect(mockedRefresh).toHaveBeenCalled());
    });

    it("remounts the add form fresh on a sponsor change, via the caller's own key (PLAN T447 ruling)", () => {
      const threeSponsors: readonly SponsorRefDto[] = [
        SPONSOR_ACME,
        SPONSOR_ACME_RADIO,
        { id: 3, name: "Riverside Diner", paused: false },
      ];

      const { rerender } = render(
        <ConfirmDialogProvider>
          <BriefsSection key="1" briefs={[]} sponsorId={1} sponsors={threeSponsors} />
        </ConfirmDialogProvider>
      );

      fireEvent.change(screen.getByLabelText("Sponsor"), { target: { value: "2" } });
      expect(screen.getByLabelText("Sponsor")).toHaveValue("2");

      // A different `key` forces React to unmount the old instance and mount a fresh one rather
      // than reusing it with new props — the form's own local sponsor choice above (2) must NOT
      // survive; the fresh mount reads the new `sponsorId` prop (3) instead.
      rerender(
        <ConfirmDialogProvider>
          <BriefsSection key="3" briefs={[]} sponsorId={3} sponsors={threeSponsors} />
        </ConfirmDialogProvider>
      );

      expect(screen.getByLabelText("Sponsor")).toHaveValue("3");
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
                'An owner-authored brief with this premise for sponsor "Acme Radio" already exists — edit it instead of creating a second one.',
              field: "premise",
            },
          }),
        },
      ]);

      render(
        <ConfirmDialogProvider>
          <BriefsSection briefs={[]} sponsorId={null} sponsors={SPONSOR_REFS} />
        </ConfirmDialogProvider>
      );

      fireEvent.change(screen.getByLabelText("Sponsor"), { target: { value: "2" } });

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
      render(<AdSpotEditor initial={null} sponsors={SPONSOR_REFS} onSaved={onSaved} onCancel={jest.fn()} />);

      fireEvent.change(screen.getByLabelText("Sponsor"), { target: { value: "1" } });
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
