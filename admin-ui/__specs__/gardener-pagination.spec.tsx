// @jest-environment jsdom
// STORY-382 — I page through a big kind at my own pace · STORY-383 AC4 — a whole cluster renders
// together (SPEC F153.9/F153.10 riders 2026-08-31 · PLAN T387 · gh-#657)
//
// BDD specification — Jest (jsdom) + @testing-library/react. `resolveGardenerPageSize` (the pure
// resolver) is spec'd directly. `GardenerPageSizePicker`/`GardenerTabs` (no hooks, no fetch) are
// exercised directly with RTL. The pager math and beyond-end/whole-cluster scenarios drive the real
// server page (`gardener/page.tsx`) end to end, mirroring gardener-tabs.spec.tsx's own harness —
// `next/headers.cookies()` and `next/navigation`'s `useRouter` are mocked, `global.fetch` is mocked
// dispatched by URL+method, and the page is `await import()`ed fresh per test.

jest.mock("next/headers", () => ({
  cookies: jest.fn<() => Promise<{ toString: () => string }>>().mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { PageSizePicker } from "@/components/ui/page-size-picker";
import { Toaster } from "@/components/ui/toast";
import { GardenerTabs } from "../app/(authed)/gardener/GardenerTabs";
import { buildGardenerHref, GARDENER_PAGE_SIZES, resolveGardenerPageSize } from "../app/(authed)/gardener/gardener-paging";
import type { GardenerDuplicateGroupDto, GardenerFindingDto, GardenerKind, GardenerOpenCounts } from "@/lib/gardener-api";

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

function finding(n: number, overrides: Partial<GardenerFindingDto["media"]> = {}): GardenerFindingDto {
  return {
    id: n,
    mediaId: 1000 + n,
    state: "open",
    evidence: {},
    openedAt: "2026-08-01T00:00:00Z",
    resolvedAt: null,
    dismissedAt: null,
    media: {
      path: `/media/${n}.flac`,
      title: `Track ${n}`,
      artist: "Artist",
      durationMs: 200000,
      plays: 1,
      rating: null,
      neverPlay: false,
      eligible: true,
      ...overrides,
    },
  };
}

/** A row-paged kind fixture (every kind but near_duplicate, STORY-382 AC6): the mock slices `all`
 * by the request's own limit/offset, `total` is always `all.length`. */
interface RowKindFixture {
  paging: "rows";
  all: GardenerFindingDto[];
}

/** A group-paged kind fixture (near_duplicate only, STORY-383 AC4): already the one page's worth of
 * rows/groups — `total` is the GROUPS count, not `findings.length` (STORY-382 AC6/AC8). */
interface GroupKindFixture {
  paging: "groups";
  findings: GardenerFindingDto[];
  duplicateGroups: GardenerDuplicateGroupDto[];
  total: number;
}

type KindFixture = RowKindFixture | GroupKindFixture;

interface FetchMockOptions {
  findings: Partial<Record<GardenerKind, KindFixture>>;
  open: GardenerOpenCounts;
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
    const url = new URL(String(input), "http://localhost");

    if (url.pathname === "/api/status") {
      return jsonResponse(200, { gardener: { open: options.open, total: 0 } });
    }

    if (url.pathname === "/api/gardener/findings") {
      const kind = url.searchParams.get("kind") as GardenerKind | null;
      const fixture = kind !== null ? options.findings[kind] : undefined;
      if (fixture === undefined) {
        throw new Error(`unexpected findings fetch for kind=${String(kind)}`);
      }

      if (fixture.paging === "groups") {
        return jsonResponse(200, {
          groups:
            fixture.findings.length > 0
              ? [{ kind, findings: fixture.findings, duplicateGroups: fixture.duplicateGroups }]
              : [],
          total: fixture.total,
        });
      }

      const limit = Number(url.searchParams.get("limit"));
      const offset = Number(url.searchParams.get("offset"));
      const page = fixture.all.slice(offset, offset + limit);
      return jsonResponse(200, {
        groups: page.length > 0 ? [{ kind, findings: page, duplicateGroups: [] }] : [],
        total: fixture.all.length,
      });
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

const SIXTY_DEAD_FILES: GardenerFindingDto[] = Array.from({ length: 60 }, (_, i) => finding(i + 1));
const THIRTY_DEAD_FILES: GardenerFindingDto[] = Array.from({ length: 30 }, (_, i) => finding(i + 1));

// ---------------------------------------------------------------------------
// Feature: page-size resolution (pure)
// ---------------------------------------------------------------------------

describe("Feature: Gardener page size resolution", () => {
  describe("Scenario: out-of-set sizes read as 25", () => {
    it("treats ?limit=999 as 25", () => {
      expect(resolveGardenerPageSize("999")).toBe(25);
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: the size picker (component)
// ---------------------------------------------------------------------------

describe("Feature: the Gardener rows-per-page picker", () => {
  describe("Scenario: the size picker", () => {
    it("offers exactly 25, 50, 100, and 250", () => {
      render(<PageSizePicker sizes={GARDENER_PAGE_SIZES} limit={25} hrefFor={(size) => buildGardenerHref("dead_file", size)} />);

      const group = screen.getByRole("group", { name: "Rows per page" });
      const links = within(group).getAllByRole("link");
      expect(links.map((link) => link.textContent)).toEqual(["25", "50", "100", "250"]);
    });

    it("writes limit=100 to the URL when 100 is picked", () => {
      render(<PageSizePicker sizes={GARDENER_PAGE_SIZES} limit={25} hrefFor={(size) => buildGardenerHref("dead_file", size)} />);

      const group = screen.getByRole("group", { name: "Rows per page" });
      expect(within(group).getByRole("link", { name: "100" })).toHaveAttribute("href", "/gardener?limit=100");
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: tab switch keeps size, resets page (component)
// ---------------------------------------------------------------------------

describe("Feature: tab switch keeps size, resets page", () => {
  describe("Scenario: tab switch keeps size, resets page", () => {
    it("keeps limit=100 in the target tab's URL", () => {
      render(<GardenerTabs activeTab="dead_file" limit={100} open={openCounts()} />);

      const nav = screen.getByRole("navigation", { name: "Gardener kinds" });
      expect(within(nav).getByRole("link", { name: "Near duplicates (0)" })).toHaveAttribute(
        "href",
        "/gardener?tab=near_duplicate&limit=100"
      );
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: Gardener pagination — driven through the real server page
// ---------------------------------------------------------------------------

describe("Feature: Gardener pagination — server page wiring", () => {
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

  describe("Scenario: the default page", () => {
    it("renders 25 rows for a 60-row kind with no paging params", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({});

      expect(screen.getAllByText(/^Track \d+$/)).toHaveLength(25);
    });

    it('reads "page 1 of 3"', async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({});

      expect(screen.getByText("Page 1 of 3")).toBeInTheDocument();
    });

    it("renders a Next anchor to page 2", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({});

      expect(screen.getByRole("link", { name: "Next" })).toHaveAttribute("href", "/gardener?page=2");
    });
  });

  describe("Scenario: a deep page from the URL", () => {
    it("renders rows 51-60 at ?page=3 of a 60-row kind", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({ page: "3" });

      expect(screen.getByText("Track 51")).toBeInTheDocument();
      expect(screen.getByText("Track 60")).toBeInTheDocument();
      expect(screen.queryByText("Track 50")).not.toBeInTheDocument();
    });

    it("renders a Previous anchor to page 2", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({ page: "3" });

      expect(screen.getByRole("link", { name: "Previous" })).toHaveAttribute("href", "/gardener?page=2");
    });

    it("renders no live Next anchor on the last page", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({ page: "3" });

      expect(screen.queryByRole("link", { name: "Next" })).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the total comes from the response", () => {
    it('derives "page N of M" from the kind-scoped response total, not /api/status', async () => {
      // status says 5 open dead files; the findings response's own total says 60 — the pager must
      // read the 60 (STORY-382 AC6/AC8), never the status figure.
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 5 }) });

      await renderGardenerPage({});

      expect(screen.getByText("Page 1 of 3")).toBeInTheDocument();
    });
  });

  describe("Scenario: a whole cluster renders together", () => {
    it("renders all members of a 4-member duplicate group in one card on one page", async () => {
      const members = [finding(1, { title: "Song X" }), finding(2, { title: "Song X (Live)" }), finding(3, { title: "Song X (Demo)" }), finding(4, { title: "Song X (Remix)" })];
      makeFetchMock({
        findings: {
          near_duplicate: {
            paging: "groups",
            findings: members,
            duplicateGroups: [{ groupKey: "grp-1", members }],
            total: 1,
          },
        },
        open: openCounts({ nearDuplicate: 4 }),
      });

      await renderGardenerPage({ tab: "near_duplicate" });

      expect(screen.getAllByText("Group grp-1")).toHaveLength(1);
      expect(screen.getByText("Song X")).toBeInTheDocument();
      expect(screen.getByText("Song X (Live)")).toBeInTheDocument();
      expect(screen.getByText("Song X (Demo)")).toBeInTheDocument();
      expect(screen.getByText("Song X (Remix)")).toBeInTheDocument();
    });
  });

  describe("Scenario: out-of-set sizes read as 25", () => {
    it("shows 25 in the picker for an out-of-set ?limit=", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({ limit: "999" });

      const group = screen.getByRole("group", { name: "Rows per page" });
      expect(within(group).getByRole("link", { name: "25" })).toHaveAttribute("aria-current", "page");
    });
  });

  describe("Scenario: a page beyond the end recovers", () => {
    it("renders the empty state at ?page=3 of a 2-page kind", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: THIRTY_DEAD_FILES } }, open: openCounts({ deadFile: 30 }) });

      await renderGardenerPage({ page: "3" });

      expect(screen.getByText("No dead files.")).toBeInTheDocument();
    });

    it("keeps the pager live so Previous reaches page 2", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: THIRTY_DEAD_FILES } }, open: openCounts({ deadFile: 30 }) });

      await renderGardenerPage({ page: "3" });

      expect(screen.getByRole("link", { name: "Previous" })).toHaveAttribute("href", "/gardener?page=2");
    });
  });

  // T387 review MED-1: `GardenerController`'s own `offset` query parameter is a C# `int?` — a
  // derived offset beyond Int32.MaxValue fails ASP.NET model binding into a 400, which SPEC
  // F153.10 rider's "never a 400" promise forbids. `resolveGardenerPaging` clamps `page` so the
  // derived offset can never reach that ceiling.
  describe("Scenario: an absurdly large ?page= never overflows the derived offset", () => {
    it("clamps the page so a huge ?page= renders a live pager instead of the error branch", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({ page: "999999999999" });

      // 2147483647 (Int32.MaxValue) / 25 (the default limit), floored, + 1 — the largest page whose
      // `(page - 1) * limit` still fits — clamped down from the requested value, one page lower.
      expect(screen.getByRole("link", { name: "Previous" })).toHaveAttribute("href", "/gardener?page=85899345");
    });
  });

  // T387 review LOW-5: the isolated-component versions of these two specs (`<GardenerTabs>`/
  // `<GardenerPageSizePicker>` rendered with no `page` prop at all) could never actually observe a
  // stale page carrying forward — neither component accepts one, so the assertion was
  // structurally guaranteed to pass even if the reset logic broke. These drive the real page with
  // `?page=2` already on the URL, so a regression that threaded the stale page through would
  // actually red them.
  describe("Scenario: tab switch and size changes really do reset the page, not just structurally", () => {
    it("keeps limit=100 and drops the current ?page= when switching tabs", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({ tab: "dead_file", page: "2", limit: "100" });

      const nav = screen.getByRole("navigation", { name: "Gardener kinds" });
      expect(within(nav).getByRole("link", { name: "Near duplicates (0)" })).toHaveAttribute(
        "href",
        "/gardener?tab=near_duplicate&limit=100"
      );
    });

    it("drops the current ?page= when a new size is picked", async () => {
      makeFetchMock({ findings: { dead_file: { paging: "rows", all: SIXTY_DEAD_FILES } }, open: openCounts({ deadFile: 60 }) });

      await renderGardenerPage({ tab: "dead_file", page: "2", limit: "100" });

      const group = screen.getByRole("group", { name: "Rows per page" });
      for (const link of within(group).getAllByRole("link")) {
        expect(link.getAttribute("href")).not.toMatch(/page=/);
      }
    });
  });
});
