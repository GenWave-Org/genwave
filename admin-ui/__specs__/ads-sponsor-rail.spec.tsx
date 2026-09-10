// @jest-environment jsdom
// SPEC F171.8, STORY-413, PLAN T447 — the Ads page groups by sponsor.
//
// Runner: Jest (jsdom) + @testing-library/react, driving the REAL `ads/page.tsx` end-to-end —
// mirrors `ads-page.spec.tsx`'s own `renderAdsPage` posture (`next/headers`'s `cookies()` and
// `next/navigation`'s `useRouter` mocked, `global.fetch` mocked by method+pathname, the page
// `await import()`ed fresh after the mocks are registered, since this project's SWC jest transform
// does not hoist `jest.mock` past a static import).
//
// Fixture: three sponsors — "Riverside Diner" (id 1), "North Hardware" (id 2, paused), "Lakeside
// Motors" (id 3, no briefs/spots at all) — with briefs and ready-tab spots split between the first
// two, so every AC1/AC2 fact has a real positive AND negative case to check.

jest.mock("next/headers", () => ({
  cookies: jest
    .fn<() => Promise<{ toString: () => string }>>()
    .mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { cleanup, render, screen, fireEvent, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import type { AdBriefDto, AdSpotDto } from "@/lib/ads-api";
import type { SponsorListItemDto, SponsorRefDto } from "@/lib/sponsors-api";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedRefresh = jest.fn<() => void>();

beforeEach(() => {
  mockedRefresh.mockClear();
  mockedUseRouter.mockReturnValue({ refresh: mockedRefresh } as unknown as ReturnType<typeof useRouter>);
});

afterEach(() => {
  jest.clearAllMocks();
  cleanup();
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SPONSOR_A_REF: SponsorRefDto = { id: 1, name: "Riverside Diner", paused: false };
const SPONSOR_B_REF: SponsorRefDto = { id: 2, name: "North Hardware", paused: true };

function sponsorListItem(overrides: Partial<SponsorListItemDto> = {}): SponsorListItemDto {
  return {
    id: 1,
    name: "Sponsor",
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

// Sponsor A carries spots in TWO states (draft + ready) so the "spots-total" fact actually
// exercises a sum, not a single passthrough value.
const SPONSORS_RESPONSE: SponsorListItemDto[] = [
  sponsorListItem({ id: 1, name: "Riverside Diner", paused: false, briefs: 3, spots: { draft: 2, ready: 1 } }),
  sponsorListItem({ id: 2, name: "North Hardware", paused: true, briefs: 1, spots: { ready: 1 } }),
  sponsorListItem({ id: 3, name: "Lakeside Motors", paused: false, briefs: 0, spots: {} }),
];

function adSpot(overrides: Partial<AdSpotDto> = {}): AdSpotDto {
  return {
    id: 1,
    sponsorId: SPONSOR_A_REF.id,
    sponsorName: SPONSOR_A_REF.name,
    sponsor: SPONSOR_A_REF,
    title: "A spot",
    brief: null,
    script: null,
    source: "owner",
    packSlug: null,
    spotSeconds: 30,
    voicePlan: null,
    bedMediaId: null,
    state: "ready",
    failReason: null,
    mediaId: null,
    createdAt: "2026-09-01T00:00:00Z",
    stateChangedAt: "2026-09-01T00:00:00Z",
    renderedAt: null,
    retiredAt: null,
    version: "1",
    job: null,
    preview: null,
    ...overrides,
  };
}

function adBrief(overrides: Partial<AdBriefDto> = {}): AdBriefDto {
  return {
    id: 1,
    packSlug: null,
    sponsor: SPONSOR_A_REF,
    premise: "A premise",
    tone: null,
    structure: null,
    enabled: true,
    createdAt: "2026-09-01T00:00:00Z",
    ...overrides,
  };
}

// Split across sponsor A and sponsor B — sponsor C is never referenced by a brief or a spot,
// proving its rail row still renders from the sponsor list alone.
const BRIEFS_FIXTURE: AdBriefDto[] = [
  adBrief({ id: 1, sponsor: SPONSOR_A_REF, premise: "Riverside's lunch special" }),
  adBrief({ id: 2, sponsor: SPONSOR_A_REF, premise: "Riverside's happy hour" }),
  adBrief({ id: 3, sponsor: SPONSOR_B_REF, premise: "North Hardware's spring sale" }),
];

const READY_SPOTS_FIXTURE: AdSpotDto[] = [
  adSpot({ id: 10, sponsor: SPONSOR_A_REF, sponsorId: SPONSOR_A_REF.id, sponsorName: SPONSOR_A_REF.name, title: "Riverside Ready Spot", state: "ready" }),
  adSpot({ id: 11, sponsor: SPONSOR_B_REF, sponsorId: SPONSOR_B_REF.id, sponsorName: SPONSOR_B_REF.name, title: "North Hardware Ready Spot", state: "ready" }),
];

// ---------------------------------------------------------------------------
// Fetch mock — a small route table (method + pathname predicate → response), mirroring
// `ads-page.spec.tsx`'s own `installFetchMock`.
// ---------------------------------------------------------------------------

interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

interface RouteHandler {
  method: string;
  match: (url: URL) => boolean;
  respond: (url: URL) => RouteResponseSpec;
}

function toResponse(spec: RouteResponseSpec): Response {
  return {
    ok: spec.status >= 200 && spec.status < 300,
    status: spec.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

function installFetchMock(handlers: RouteHandler[]): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = new URL(String(input), "http://localhost");
    const handler = handlers.find((h) => h.method === method && h.match(url));
    if (handler === undefined) {
      throw new Error(`unexpected fetch call: ${method} ${url.pathname}${url.search}`);
    }
    return toResponse(handler.respond(url));
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

/** `GET /api/sponsors` — the three-sponsor fixture by default; every test needs this route since
 * `page.tsx` fetches it on every render, tab or not. A caller may hand an override body (an empty
 * array, for the zero-sponsors scenario). */
function sponsorsRoute(body: SponsorListItemDto[] = SPONSORS_RESPONSE): RouteHandler {
  return {
    method: "GET",
    match: (u) => u.pathname === "/api/sponsors",
    respond: () => ({ status: 200, body }),
  };
}

/** `GET /api/ads` — filters the fixture by `state` and (when present) `sponsorId`, the same way
 * `AdsController.List` does server-side; this is what lets AC2's "the fetch mock received
 * `sponsorId=<A>`" fact and the "only A's rows render" fact both come from one honest route. */
function adsRoute(spots: AdSpotDto[]): RouteHandler {
  return {
    method: "GET",
    match: (u) => u.pathname === "/api/ads",
    respond: (u) => {
      const state = u.searchParams.get("state");
      const sponsorIdParam = u.searchParams.get("sponsorId");
      const items = spots.filter(
        (spot) => spot.state === state && (sponsorIdParam === null || String(spot.sponsorId) === sponsorIdParam)
      );
      return { status: 200, body: { items, total: items.length } };
    },
  };
}

/** `GET /api/ad-briefs` — a bare, unfiltered array (no `sponsorId` filter exists on this endpoint;
 * `page.tsx` scopes the Briefs tab client-side by `brief.sponsor.id`). */
function briefsRoute(briefs: AdBriefDto[]): RouteHandler {
  return {
    method: "GET",
    match: (u) => u.pathname === "/api/ad-briefs",
    respond: () => ({ status: 200, body: briefs }),
  };
}

/** `GET /api/ads` returning one item alongside a `total` far past it — the real API returns one
 * PAGE of items plus the FULL count (`AdsController.List`'s own `{ items, total }` envelope), so
 * proving a second page exists never needs 51 fixture spots. */
function pagedAdsRoute(): RouteHandler {
  return {
    method: "GET",
    match: (u) => u.pathname === "/api/ads",
    respond: () => ({
      status: 200,
      body: { items: [adSpot({ id: 900, title: "Page one spot", state: "ready" })], total: 999 },
    }),
  };
}

async function renderAdsPage(sp: Record<string, string>): Promise<ReturnType<typeof render>> {
  const { default: AdsPage } = await import("../app/(authed)/ads/page");
  const node = await AdsPage({ searchParams: Promise.resolve(sp) });
  return render(<ConfirmDialogProvider>{node}</ConfirmDialogProvider>);
}

function sponsorRail(): HTMLElement {
  return screen.getByRole("navigation", { name: "Sponsors" });
}

/** The rail row (the plain `<a>`) carrying a given sponsor's name — scoped lookups off of it are
 * what let the count/badge facts below target the RIGHT row instead of asserting on the page as a
 * whole. */
function railRow(name: string): HTMLElement {
  return within(sponsorRail()).getByText(name).closest("a") as HTMLElement;
}

// ---------------------------------------------------------------------------

describe("Feature: The Ads page groups by sponsor", () => {
  describe("Scenario: the sponsor rail lists every sponsor", () => {
    it("renders one rail row per sponsor with its name — AC1", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready" });

      expect(within(sponsorRail()).getByText("Riverside Diner")).toBeInTheDocument();
      expect(within(sponsorRail()).getByText("North Hardware")).toBeInTheDocument();
      expect(within(sponsorRail()).getByText("Lakeside Motors")).toBeInTheDocument();
    });

    it("shows the briefs count on each row — AC1", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready" });

      expect(within(railRow("Riverside Diner")).getByText(/3 briefs/)).toBeInTheDocument();
      expect(within(railRow("North Hardware")).getByText(/1 briefs/)).toBeInTheDocument();
      expect(within(railRow("Lakeside Motors")).getByText(/0 briefs/)).toBeInTheDocument();
    });

    it("shows the spots-total count on each row — AC1", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready" });

      // Riverside Diner's fixture spreads across two states (draft: 2, ready: 1) — the row shows
      // the SUM, 3, not just the ready-tab count.
      expect(within(railRow("Riverside Diner")).getByText(/3 spots/)).toBeInTheDocument();
      expect(within(railRow("North Hardware")).getByText(/1 spots/)).toBeInTheDocument();
      expect(within(railRow("Lakeside Motors")).getByText(/0 spots/)).toBeInTheDocument();
    });

    it("shows a Paused badge on the paused sponsor only — AC1", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready" });

      expect(within(railRow("North Hardware")).getByText("Paused")).toBeInTheDocument();
      expect(within(railRow("Riverside Diner")).queryByText("Paused")).not.toBeInTheDocument();
      expect(within(railRow("Lakeside Motors")).queryByText("Paused")).not.toBeInTheDocument();
      expect(within(sponsorRail()).getAllByText("Paused")).toHaveLength(1);
    });
  });

  describe("Scenario: selecting a sponsor scopes the right pane", () => {
    it("shows only the selected sponsor's briefs — AC2", async () => {
      installFetchMock([sponsorsRoute(), briefsRoute(BRIEFS_FIXTURE)]);

      await renderAdsPage({ tab: "briefs", sponsor: "1" });

      expect(screen.getByText("Riverside's lunch special")).toBeInTheDocument();
      expect(screen.getByText("Riverside's happy hour")).toBeInTheDocument();
      expect(screen.queryByText("North Hardware's spring sale")).not.toBeInTheDocument();
    });

    it("shows only the selected sponsor's spots — AC2", async () => {
      const mockFetch = installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready", sponsor: "1" });

      const callIndex = mockFetch.mock.calls.findIndex(
        ([input]) => new URL(String(input), "http://localhost").pathname === "/api/ads"
      );
      expect(callIndex).toBeGreaterThan(-1);
      const calledUrl = new URL(String(mockFetch.mock.calls[callIndex]?.[0]), "http://localhost");
      expect(calledUrl.searchParams.get("sponsorId")).toBe("1");

      expect(screen.getByText("Riverside Ready Spot")).toBeInTheDocument();
      expect(screen.queryByText("North Hardware Ready Spot")).not.toBeInTheDocument();
    });

    it("opens New spot with the selected sponsor preselected — AC2", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready", sponsor: "1" });

      fireEvent.click(screen.getByRole("button", { name: "New spot…" }));

      expect(screen.getByLabelText("Sponsor")).toHaveValue("1");
    });
  });

  describe("Scenario: the rail's own links carry the selection", () => {
    it("keeps a sponsor row's own href pointed at itself, and 'All sponsors' at no selection, while another sponsor is selected", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready", sponsor: "1" });

      expect(railRow("North Hardware")).toHaveAttribute("href", "/ads?tab=ready&sponsor=2");
      expect(within(sponsorRail()).getByText("All sponsors").closest("a")).toHaveAttribute("href", "/ads?tab=ready");
    });
  });

  describe("Scenario: the selected row is marked current", () => {
    it("marks only the selected sponsor's row aria-current — not 'All sponsors', not any other row", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready", sponsor: "2" });

      expect(railRow("North Hardware")).toHaveAttribute("aria-current", "true");
      expect(railRow("Riverside Diner")).not.toHaveAttribute("aria-current");
      expect(railRow("Lakeside Motors")).not.toHaveAttribute("aria-current");
      expect(within(sponsorRail()).getByText("All sponsors").closest("a")).not.toHaveAttribute("aria-current");
    });

    it("marks 'All sponsors' current, and no row, when nothing is selected", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready" });

      expect(within(sponsorRail()).getByText("All sponsors").closest("a")).toHaveAttribute("aria-current", "true");
      expect(railRow("Riverside Diner")).not.toHaveAttribute("aria-current");
      expect(railRow("North Hardware")).not.toHaveAttribute("aria-current");
      expect(railRow("Lakeside Motors")).not.toHaveAttribute("aria-current");
    });
  });

  describe("Scenario: zero sponsors", () => {
    it("shows one plain sentence in the rail, and still renders the tab's own pane, when there are no sponsors", async () => {
      installFetchMock([sponsorsRoute([]), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready" });

      expect(screen.getByText("No sponsors yet.")).toBeInTheDocument();
      expect(screen.getByRole("region", { name: "Ready" })).toBeInTheDocument();
    });
  });

  describe("Scenario: an unknown sponsor id in the URL", () => {
    it("falls back to 'All sponsors' when ?sponsor= matches no fetched sponsor — PLAN T447 ruling", async () => {
      const mockFetch = installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);

      await renderAdsPage({ tab: "ready", sponsor: "999" });

      expect(within(sponsorRail()).getByText("All sponsors").closest("a")).toHaveAttribute("aria-current", "true");
      expect(railRow("Riverside Diner")).not.toHaveAttribute("aria-current");
      expect(railRow("North Hardware")).not.toHaveAttribute("aria-current");
      expect(railRow("Lakeside Motors")).not.toHaveAttribute("aria-current");

      const adsCalls = mockFetch.mock.calls.filter(
        ([input]) => new URL(String(input), "http://localhost").pathname === "/api/ads"
      );
      const lastAdsUrl = new URL(String(adsCalls[adsCalls.length - 1]?.[0]), "http://localhost");
      expect(lastAdsUrl.searchParams.get("sponsorId")).toBeNull();
    });

    it("re-fetches once for an unknown sponsor id, but costs no extra round trip for a known one — PLAN T447 ruling", async () => {
      const unknownFetch = installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);
      await renderAdsPage({ tab: "ready", sponsor: "999" });
      expect(unknownFetch).toHaveBeenCalledTimes(3);
      cleanup();

      const knownFetch = installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);
      await renderAdsPage({ tab: "ready", sponsor: "1" });
      expect(knownFetch).toHaveBeenCalledTimes(2);
    });
  });

  describe("Scenario: pagination carries the selection", () => {
    it("keeps ?sponsor= on the Next link and the page-size picker's own links", async () => {
      installFetchMock([sponsorsRoute(), pagedAdsRoute()]);

      await renderAdsPage({ tab: "ready", sponsor: "1" });

      const pagerNav = screen.getByRole("navigation", { name: "Pagination" });
      expect(within(pagerNav).getByRole("link", { name: "Next" })).toHaveAttribute(
        "href",
        "/ads?tab=ready&sponsor=1&page=2"
      );

      const sizeGroup = screen.getByRole("group", { name: "Rows per page" });
      expect(within(sizeGroup).getByRole("link", { name: "200" })).toHaveAttribute(
        "href",
        "/ads?tab=ready&limit=200&sponsor=1"
      );
    });
  });

  describe("Scenario: the add-brief form resets on a sponsor change", () => {
    it("clears an in-progress Premise draft when the rail's own selection changes — PLAN T447 ruling", async () => {
      installFetchMock([sponsorsRoute(), briefsRoute([])]);

      const { rerender } = await renderAdsPage({ tab: "briefs", sponsor: "1" });
      fireEvent.change(screen.getByLabelText("Premise"), { target: { value: "draft in progress" } });
      expect(screen.getByLabelText("Premise")).toHaveValue("draft in progress");

      // Same container, a fresh call to the real page's own default export for a different
      // sponsor — this is `page.tsx`'s own `key={sponsorId ?? "all"}` wiring under test, not a
      // fixture-level `rerender` of the add form on its own.
      const { default: AdsPage } = await import("../app/(authed)/ads/page");
      const nextNode = await AdsPage({ searchParams: Promise.resolve({ tab: "briefs", sponsor: "2" }) });
      rerender(<ConfirmDialogProvider>{nextNode}</ConfirmDialogProvider>);

      expect(screen.getByLabelText("Premise")).toHaveValue("");
    });
  });

  describe("Scenario: no jargon", () => {
    it("the rendered page contains neither 'brand' nor 'advertiser' — AC3", async () => {
      installFetchMock([sponsorsRoute(), adsRoute(READY_SPOTS_FIXTURE)]);
      await renderAdsPage({ tab: "ready" });
      expect(document.body.textContent).not.toMatch(/brand/i);
      expect(document.body.textContent).not.toMatch(/advertiser/i);
      cleanup();

      installFetchMock([sponsorsRoute(), briefsRoute(BRIEFS_FIXTURE)]);
      await renderAdsPage({ tab: "briefs" });
      expect(document.body.textContent).not.toMatch(/brand/i);
      expect(document.body.textContent).not.toMatch(/advertiser/i);
    });
  });
});
