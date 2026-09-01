// @jest-environment jsdom
// STORY-381 — I browse the queue one kind at a time (SPEC F153.10 rider 2026-08-31 · PLAN T387 · gh-#657)
//
// BDD specification — Jest (jsdom) + @testing-library/react. `GardenerTabs` itself (no hooks, no
// fetch) is exercised directly with RTL, mirroring `catalog-kind-tabs.spec.tsx`'s own harness for
// `PersonaCatalogTabs`. The tab-scoped fetch/activation/purge/refresh scenarios drive the real
// server page (`gardener/page.tsx`) end to end — `next/headers.cookies()` and `next/navigation`'s
// `useRouter` are mocked (the `persona-catalog-page.spec.tsx` convention), `global.fetch` is mocked
// dispatched by URL+method, and the page is `await import()`ed fresh per test AFTER the mocks are
// registered (this project's SWC jest transform does not hoist `jest.mock` past a static import).
//
// gh-#655 rides this story (AC6): the purge trigger's verb-object label is pinned here.

jest.mock("next/headers", () => ({
  cookies: jest.fn<() => Promise<{ toString: () => string }>>().mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { GardenerTabs } from "../app/(authed)/gardener/GardenerTabs";
import type { GardenerFindingDto, GardenerKind, GardenerOpenCounts } from "@/lib/gardener-api";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;
const mockedRefresh = jest.fn<() => void>();

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function openCounts(overrides: Partial<GardenerOpenCounts> = {}): GardenerOpenCounts {
  return { deadFile: 0, nearDuplicate: 0, staleMetadata: 0, unreachable: 0, shelfDust: 0, ...overrides };
}

function finding(id: number, mediaId: number, title: string): GardenerFindingDto {
  return {
    id,
    mediaId,
    state: "open",
    evidence: { reason: "failed" },
    openedAt: "2026-08-01T00:00:00Z",
    resolvedAt: null,
    dismissedAt: null,
    media: {
      path: `/media/${mediaId}.flac`,
      title,
      artist: "Artist",
      durationMs: 200000,
      plays: 1,
      rating: null,
      neverPlay: false,
      eligible: true,
    },
  };
}

interface KindFixture {
  rows: GardenerFindingDto[];
  total: number;
}

interface FetchMockOptions {
  /** Per-kind findings — a kind absent here throws if requested (catches a stray fetch). */
  findings: Partial<Record<GardenerKind, KindFixture>>;
  /** `null` simulates a failed status fetch (degrades the tab badges, never the page itself). */
  open: GardenerOpenCounts | null;
}

function jsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
  } as unknown as Response;
}

function makeFetchMock(options: FetchMockOptions): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input) => {
    // `apiGet` (page.tsx's own reads) always hands an absolute BACKEND_URL-prefixed request; the
    // dismiss verb (GardenerRow, browser-side) fetches a bare relative path instead — a base origin
    // lets URL() parse both the same way real `fetch()` resolution would.
    const url = new URL(String(input), "http://localhost");

    if (url.pathname === "/api/status") {
      return options.open === null
        ? jsonResponse(500, {})
        : jsonResponse(200, { gardener: { open: options.open, total: 0 } });
    }

    if (url.pathname === "/api/gardener/findings") {
      const kind = url.searchParams.get("kind") as GardenerKind | null;
      const fixture = kind !== null ? options.findings[kind] : undefined;
      if (fixture === undefined) {
        throw new Error(`unexpected findings fetch for kind=${String(kind)}`);
      }
      return jsonResponse(200, {
        groups: fixture.rows.length > 0 ? [{ kind, findings: fixture.rows, duplicateGroups: [] }] : [],
        total: fixture.total,
      });
    }

    if (url.pathname.endsWith("/dismiss")) {
      return jsonResponse(204, {});
    }

    throw new Error(`unexpected fetch call: ${String(input)}`);
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

async function renderGardenerPage(sp: Record<string, string>): Promise<ReturnType<typeof render>> {
  const { default: GardenerPage } = await import("../app/(authed)/gardener/page");
  const node = await GardenerPage({ searchParams: Promise.resolve(sp) });
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

// ---------------------------------------------------------------------------
// Feature: Gardener kind tabs — the strip itself
// ---------------------------------------------------------------------------

describe("Feature: Gardener kind tabs", () => {
  describe("Scenario: five tabs, badged from status", () => {
    it("renders five tabs in the fixed kind order", () => {
      render(<GardenerTabs activeTab="dead_file" limit={25} open={openCounts()} />);

      const nav = screen.getByRole("navigation", { name: "Gardener kinds" });
      const links = within(nav).getAllByRole("link");
      expect(links.map((link) => link.textContent)).toEqual([
        "Dead files (0)",
        "Near duplicates (0)",
        "Stale metadata (0)",
        "Unreachable (0)",
        "Shelf dust (0)",
      ]);
    });

    it("labels each tab with its kind's open count from /api/status", () => {
      render(
        <GardenerTabs
          activeTab="dead_file"
          limit={25}
          open={openCounts({ deadFile: 3, nearDuplicate: 7, staleMetadata: 1, unreachable: 4, shelfDust: 12 })}
        />
      );

      const nav = screen.getByRole("navigation", { name: "Gardener kinds" });
      expect(within(nav).getByRole("link", { name: "Dead files (3)" })).toBeInTheDocument();
      expect(within(nav).getByRole("link", { name: "Near duplicates (7)" })).toBeInTheDocument();
      expect(within(nav).getByRole("link", { name: "Stale metadata (1)" })).toBeInTheDocument();
      expect(within(nav).getByRole("link", { name: "Unreachable (4)" })).toBeInTheDocument();
      expect(within(nav).getByRole("link", { name: "Shelf dust (12)" })).toBeInTheDocument();
    });
  });

  describe("Scenario: an empty kind names itself", () => {
    it("keeps the empty kind's badge at 0", () => {
      render(<GardenerTabs activeTab="dead_file" limit={25} open={openCounts({ unreachable: 0 })} />);

      const nav = screen.getByRole("navigation", { name: "Gardener kinds" });
      expect(within(nav).getByRole("link", { name: "Unreachable (0)" })).toBeInTheDocument();
    });
  });

  describe("Scenario: the URL owns the active tab", () => {
    it("renders each tab as a link to its own ?tab= URL", () => {
      render(<GardenerTabs activeTab="dead_file" limit={25} open={openCounts()} />);

      const nav = screen.getByRole("navigation", { name: "Gardener kinds" });
      expect(within(nav).getByRole("link", { name: "Dead files (0)" })).toHaveAttribute("href", "/gardener");
      expect(within(nav).getByRole("link", { name: "Near duplicates (0)" })).toHaveAttribute(
        "href",
        "/gardener?tab=near_duplicate"
      );
      expect(within(nav).getByRole("link", { name: "Stale metadata (0)" })).toHaveAttribute(
        "href",
        "/gardener?tab=stale_metadata"
      );
      expect(within(nav).getByRole("link", { name: "Unreachable (0)" })).toHaveAttribute("href", "/gardener?tab=unreachable");
      expect(within(nav).getByRole("link", { name: "Shelf dust (0)" })).toHaveAttribute("href", "/gardener?tab=shelf_dust");
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: Gardener kind tabs — driven through the real server page
// ---------------------------------------------------------------------------

describe("Feature: Gardener kind tabs — server page wiring", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedRefresh.mockClear();
    mockedUseRouter.mockReturnValue({ refresh: mockedRefresh } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: a tab shows only its own kind", () => {
    it("fetches the active tab's kind with kind=<tab>&state=open", async () => {
      const mockFetch = makeFetchMock({
        findings: { near_duplicate: { rows: [], total: 0 } },
        open: openCounts(),
      });

      await renderGardenerPage({ tab: "near_duplicate" });

      const call = mockFetch.mock.calls.find(([input]) => String(input).includes("/api/gardener/findings"));
      expect(call).toBeDefined();
      const url = new URL(String(call?.[0]));
      expect(url.searchParams.get("kind")).toBe("near_duplicate");
      expect(url.searchParams.get("state")).toBe("open");
    });

    it("renders only the active kind's rows", async () => {
      makeFetchMock({
        findings: { dead_file: { rows: [finding(1, 101, "Dead Track")], total: 1 } },
        open: openCounts({ deadFile: 1 }),
      });

      await renderGardenerPage({});

      expect(screen.getByText("Dead Track")).toBeInTheDocument();
      // Only the active kind's own section renders — never a second kind's section alongside it.
      expect(screen.getByRole("region", { name: "Dead files" })).toBeInTheDocument();
      expect(screen.queryByRole("region", { name: "Near duplicates" })).not.toBeInTheDocument();
      expect(screen.queryByRole("region", { name: "Stale metadata" })).not.toBeInTheDocument();
      expect(screen.queryByRole("region", { name: "Unreachable" })).not.toBeInTheDocument();
      expect(screen.queryByRole("region", { name: "Shelf dust" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the URL owns the active tab", () => {
    it("activates the tab named by ?tab=", async () => {
      makeFetchMock({
        findings: { shelf_dust: { rows: [], total: 0 } },
        open: openCounts(),
      });

      await renderGardenerPage({ tab: "shelf_dust" });

      expect(screen.getByRole("link", { name: "Shelf dust (0)" })).toHaveAttribute("aria-current", "page");
    });
  });

  describe("Scenario: an empty kind names itself", () => {
    it("renders the kind's own empty state when it has zero open findings", async () => {
      makeFetchMock({
        findings: { unreachable: { rows: [], total: 0 } },
        open: openCounts(),
      });

      await renderGardenerPage({ tab: "unreachable" });

      expect(screen.getByText("Nothing unreachable.")).toBeInTheDocument();
    });
  });

  describe("Scenario: purge lives on the dead-files tab", () => {
    it("renders the purge action in the dead-files tab header", async () => {
      makeFetchMock({
        findings: { dead_file: { rows: [finding(1, 101, "Dead Track")], total: 1 } },
        open: openCounts({ deadFile: 1 }),
      });

      await renderGardenerPage({});

      expect(screen.getByRole("button", { name: /purge/i })).toBeInTheDocument();
    });

    it("gives the purge trigger a verb-object label, never a status reading (gh-#655)", async () => {
      makeFetchMock({
        findings: { dead_file: { rows: [finding(1, 101, "Dead Track")], total: 1 } },
        open: openCounts({ deadFile: 1 }),
      });

      await renderGardenerPage({});

      expect(screen.getByRole("button", { name: "Purge dead tracks…" })).toBeInTheDocument();
    });

    it("renders no purge action on any other tab", async () => {
      makeFetchMock({
        findings: { unreachable: { rows: [finding(1, 101, "Broken link")], total: 1 } },
        open: openCounts({ unreachable: 1 }),
      });

      await renderGardenerPage({ tab: "unreachable" });

      expect(screen.queryByRole("button", { name: /purge/i })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: verbs refresh the page", () => {
    it("refreshes via router.refresh after a dismiss completes", async () => {
      makeFetchMock({
        findings: { dead_file: { rows: [finding(1, 101, "Dead Track")], total: 1 } },
        open: openCounts({ deadFile: 1 }),
      });

      await renderGardenerPage({});

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Dismiss" }));
        await Promise.resolve();
      });
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Dismiss" }));
        await Promise.resolve();
      });

      await waitFor(() => expect(mockedRefresh).toHaveBeenCalled());
    });
  });

  describe("Scenario: default and unknown tab fall back silently", () => {
    it("activates the first tab in kind order when ?tab= is absent", async () => {
      makeFetchMock({
        findings: { dead_file: { rows: [], total: 0 } },
        open: openCounts(),
      });

      await renderGardenerPage({});

      expect(screen.getByRole("link", { name: "Dead files (0)" })).toHaveAttribute("aria-current", "page");
    });

    it("activates the first tab in kind order when ?tab= is unrecognized, with no error", async () => {
      makeFetchMock({
        findings: { dead_file: { rows: [], total: 0 } },
        open: openCounts(),
      });

      await renderGardenerPage({ tab: "not-a-real-kind" });

      expect(screen.getByRole("link", { name: "Dead files (0)" })).toHaveAttribute("aria-current", "page");
      expect(screen.queryByText(/error/i)).not.toBeInTheDocument();
    });
  });

  // T387 review MED-4: the two unspecced degrade paths — the `open: null` fixture branch
  // `makeFetchMock` above already supports becomes reachable here for the first time.
  describe("Scenario: the status fetch fails (sad path)", () => {
    it("still renders the tab strip unbadged, with rows and a working pager", async () => {
      makeFetchMock({
        findings: { dead_file: { rows: [finding(1, 101, "Track A")], total: 30 } },
        open: null,
      });

      await renderGardenerPage({});

      const nav = screen.getByRole("navigation", { name: "Gardener kinds" });
      expect(within(nav).getAllByRole("link").map((link) => link.textContent)).toEqual([
        "Dead files",
        "Near duplicates",
        "Stale metadata",
        "Unreachable",
        "Shelf dust",
      ]);
      expect(screen.getByRole("link", { name: "Next" })).toHaveAttribute("href", "/gardener?page=2");
    });
  });

  describe("Scenario: the findings fetch fails (sad path)", () => {
    it('shows "Unable to load the Gardener queue." with the tab strip still present', async () => {
      const fn = jest.fn<typeof fetch>().mockImplementation(async (input) => {
        const url = new URL(String(input), "http://localhost");
        if (url.pathname === "/api/status") return jsonResponse(200, { gardener: { open: openCounts(), total: 0 } });
        if (url.pathname === "/api/gardener/findings") return jsonResponse(500, {});
        throw new Error(`unexpected fetch call: ${String(input)}`);
      });
      global.fetch = fn as unknown as typeof fetch;

      await renderGardenerPage({});

      expect(screen.getByText("Unable to load the Gardener queue.")).toBeInTheDocument();
      expect(screen.getByRole("navigation", { name: "Gardener kinds" })).toBeInTheDocument();
    });
  });
});
