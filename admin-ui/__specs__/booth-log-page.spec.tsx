// @jest-environment jsdom
// PLAN T40 — Admin UI: booth feed page (newest-first, paged) (STORY-195, SPEC F72.1-F72.2)
//
// Runner: Jest (jsdom) + @testing-library/react. Drives BoothLogView (the client component
// page.tsx renders) with a mocked global.fetch dispatched by the `before` query param — mirrors
// live-on-air-view.spec.tsx / dashboard-page.spec.tsx's installFetchMock style, extended with a
// per-cursor page map since this feed, unlike Live/Dashboard's single-shot pollers, also fetches
// additional "Load more" pages.
//
// The sidebar-entry scenario at the bottom mirrors app-shell.spec.tsx's module-mock structure:
// next/jest's SWC transform does not hoist jest.mock() above ES import statements the way
// babel-jest does, so both mocks are declared here, before any import, and the Sidebar itself is
// pulled in via a dynamic import inside that one test rather than a static import at module top.

jest.mock("next/navigation", () => ({
  usePathname: jest.fn(),
}));

jest.mock("@/app/login/actions", () => ({
  logout: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, act, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { usePathname } from "next/navigation";
import { BoothLogView } from "../app/(authed)/booth-log/BoothLogView";

const mockedUsePathname = jest.requireMock<{ usePathname: typeof usePathname }>("next/navigation")
  .usePathname as jest.MockedFunction<typeof usePathname>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const ISO_NOW = "2026-01-01T12:00:00.000Z";

interface EntryOverrides {
  occurredAt?: string;
  kind?: string;
  summary?: string;
}

function makeEntry(overrides: EntryOverrides = {}) {
  return {
    occurredAt: "2026-01-01T10:04:00.000Z",
    kind: "track-started",
    summary: "Started 'Astral Plane' by Valerie June",
    ...overrides,
  };
}

interface PageBody {
  entries: ReturnType<typeof makeEntry>[];
  nextBefore: string | null;
}

function page(entries: ReturnType<typeof makeEntry>[], nextBefore: string | null = null): PageBody {
  return { entries, nextBefore };
}

type MockResult = { kind: "ok"; status?: number; body: unknown } | { kind: "network-error" };

function ok(body: unknown, status = 200): MockResult {
  return { kind: "ok", status, body };
}

function networkError(): MockResult {
  return { kind: "network-error" };
}

interface FetchState {
  head: MockResult;
  /** Keyed by the literal cursor string a "Load more" request should send as `?before=`. */
  pages: Record<string, MockResult>;
}

function installFetchMock(initial: FetchState) {
  const state: FetchState = { head: initial.head, pages: { ...initial.pages } };
  const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
    const url = String(input);
    const beforeMatch = /before=([^&]+)/.exec(url);
    const result = beforeMatch ? state.pages[decodeURIComponent(beforeMatch[1])] : state.head;

    if (result === undefined || result.kind === "network-error") {
      return Promise.reject(new Error("network error"));
    }
    const status = result.status ?? 200;
    return Promise.resolve({
      ok: status >= 200 && status < 300,
      status,
      json: () => Promise.resolve(result.body),
    } as Response);
  });
  global.fetch = fn as unknown as typeof fetch;
  return { fn, state };
}

/** Flushes the initial-mount poll (or any already-scheduled microtasks) without advancing time. */
async function flush(): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(0);
  });
}

/** Advances fake time and flushes the resulting fetch/json promise chain(s). */
async function advance(ms: number): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
}

beforeEach(() => {
  jest.useFakeTimers({ now: new Date(ISO_NOW) });
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: Booth log
// ---------------------------------------------------------------------------

describe("Feature: Booth log", () => {
  // -------------------------------------------------------------------------
  describe("Scenario: narrative feed rows", () => {
    it("renders occurred-at, a kind badge, and the summary for each row, newest first", async () => {
      const entries = [
        makeEntry({ occurredAt: "2026-01-01T10:10:00.000Z", kind: "track-started", summary: "Started 'C'" }),
        makeEntry({ occurredAt: "2026-01-01T10:05:00.000Z", kind: "patter-aired", summary: "Patter aired (outro)" }),
        makeEntry({
          occurredAt: "2026-01-01T10:00:00.000Z",
          kind: "mode-changed",
          summary: "LLM degradation: Normal → Reduced (timeout)",
        }),
      ];
      installFetchMock({ head: ok(page(entries)), pages: {} });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      const rows = screen.getAllByRole("row");
      expect(rows).toHaveLength(entries.length + 1); // +1 header row

      expect(screen.getByText("Track started")).toBeInTheDocument();
      expect(screen.getByText("Patter aired")).toBeInTheDocument();
      expect(screen.getByText("Mode changed")).toBeInTheDocument();
      expect(screen.getByText("Started 'C'")).toBeInTheDocument();
      expect(screen.getByText("LLM degradation: Normal → Reduced (timeout)")).toBeInTheDocument();

      // Wire order is preserved exactly (newest-first, SPEC F72.2) — no client-side re-sort.
      const rowTexts = rows.slice(1).map((row) => row.textContent ?? "");
      expect(rowTexts[0]).toContain("Started 'C'");
      expect(rowTexts[rowTexts.length - 1]).toContain("LLM degradation");
    });

    it("renders an unrecognized kind as its own raw text rather than dropping the row", async () => {
      const entries = [makeEntry({ kind: "future-event", summary: "Something new happened" })];
      installFetchMock({ head: ok(page(entries)), pages: {} });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("future-event")).toBeInTheDocument();
      expect(screen.getByText("Something new happened")).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: keyset paging via Load more", () => {
    it("passes the head page's nextBefore back and appends the returned rows below the current ones", async () => {
      const headEntries = [makeEntry({ occurredAt: "2026-01-01T10:10:00.000Z", summary: "Head row" })];
      const olderEntries = [makeEntry({ occurredAt: "2026-01-01T09:00:00.000Z", summary: "Older row" })];
      const { fn } = installFetchMock({
        head: ok(page(headEntries, "TICKS_1_1")),
        pages: { TICKS_1_1: ok(page(olderEntries, null)) },
      });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      const loadMoreButton = screen.getByRole("button", { name: "Load more" });
      fireEvent.click(loadMoreButton);
      await flush();

      expect(screen.getByText("Head row")).toBeInTheDocument();
      expect(screen.getByText("Older row")).toBeInTheDocument();
      // nextBefore came back null — the oldest page has been reached, control disappears.
      expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();

      const calledUrls = fn.mock.calls.map((call) => String(call[0]));
      expect(calledUrls.some((url) => url.includes("before=TICKS_1_1"))).toBe(true);
    });

    it("chains the next Load more click off the newly returned cursor, not the original one", async () => {
      const headEntries = [makeEntry({ occurredAt: "2026-01-01T10:10:00.000Z", summary: "Head row" })];
      const page2Entries = [makeEntry({ occurredAt: "2026-01-01T09:00:00.000Z", summary: "Page 2 row" })];
      const page3Entries = [makeEntry({ occurredAt: "2026-01-01T08:00:00.000Z", summary: "Page 3 row" })];
      const { fn } = installFetchMock({
        head: ok(page(headEntries, "CURSOR_2")),
        pages: {
          CURSOR_2: ok(page(page2Entries, "CURSOR_3")),
          CURSOR_3: ok(page(page3Entries, null)),
        },
      });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      fireEvent.click(screen.getByRole("button", { name: "Load more" }));
      await flush();
      expect(screen.getByText("Page 2 row")).toBeInTheDocument();

      fireEvent.click(screen.getByRole("button", { name: "Load more" }));
      await flush();
      expect(screen.getByText("Page 3 row")).toBeInTheDocument();

      const calledUrls = fn.mock.calls.map((call) => String(call[0]));
      expect(calledUrls.some((url) => url.includes("before=CURSOR_2"))).toBe(true);
      expect(calledUrls.some((url) => url.includes("before=CURSOR_3"))).toBe(true);
      expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
    });

    it("hides Load more entirely when the head page is already the only page", async () => {
      installFetchMock({ head: ok(page([makeEntry()], null)), pages: {} });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      expect(screen.queryByRole("button", { name: "Load more" })).not.toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: auto-refresh collapses loaded older pages (documented simplification)", () => {
    it("drops loaded older pages back to just the head page on the next poll tick", async () => {
      const headEntries = [makeEntry({ occurredAt: "2026-01-01T10:10:00.000Z", summary: "Head row" })];
      const olderEntries = [makeEntry({ occurredAt: "2026-01-01T09:00:00.000Z", summary: "Older row" })];
      const { state } = installFetchMock({
        head: ok(page(headEntries, "TICKS_1_1")),
        pages: { TICKS_1_1: ok(page(olderEntries, null)) },
      });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      fireEvent.click(screen.getByRole("button", { name: "Load more" }));
      await flush();
      expect(screen.getByText("Older row")).toBeInTheDocument();

      // Same head payload on the next tick — the collapse is unconditional (matches usePoll's
      // full-replace idiom), not conditioned on the payload having actually changed.
      state.head = ok(page(headEntries, "TICKS_1_1"));
      await advance(12000);

      expect(screen.queryByText("Older row")).not.toBeInTheDocument();
      expect(screen.getByText("Head row")).toBeInTheDocument();
      // The collapsed head page's own nextBefore is still paging-able afterward.
      expect(screen.getByRole("button", { name: "Load more" })).toBeInTheDocument();
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: shares the dashboard/live poll cadence family", () => {
    it("polls the head page again after 12s and pauses while the tab is hidden", async () => {
      Object.defineProperty(document, "hidden", { value: false, configurable: true });
      const { fn } = installFetchMock({ head: ok(page([])), pages: {} });

      render(<BoothLogView timeZone="UTC" />);
      await flush();
      const initialCalls = fn.mock.calls.length;

      await advance(12000);
      expect(fn.mock.calls.length).toBeGreaterThan(initialCalls);

      Object.defineProperty(document, "hidden", { value: true, configurable: true });
      await act(async () => {
        document.dispatchEvent(new Event("visibilitychange"));
      });
      const countAfterHide = fn.mock.calls.length;

      await advance(15000);
      expect(fn.mock.calls.length).toBe(countAfterHide);

      Object.defineProperty(document, "hidden", { value: false, configurable: true });
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: rejecting an empty log and a poll failure with designed states (sad path)", () => {
    it("renders the EmptyState explaining entries appear as events happen", async () => {
      installFetchMock({ head: ok(page([])), pages: {} });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("Nothing in the booth log yet")).toBeInTheDocument();
    });

    it("shows an unavailable message before the first successful poll", async () => {
      installFetchMock({ head: networkError(), pages: {} });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      expect(screen.getByText("Booth log — unavailable")).toBeInTheDocument();
    });

    it("degrades quietly on a later poll failure without discarding loaded entries", async () => {
      const entries = [makeEntry({ summary: "Started 'Astral Plane' by Valerie June" })];
      const { state } = installFetchMock({ head: ok(page(entries)), pages: {} });

      render(<BoothLogView timeZone="UTC" />);
      await flush();
      expect(screen.getByText("Started 'Astral Plane' by Valerie June")).toBeInTheDocument();

      state.head = networkError();
      await advance(12000);

      expect(screen.getByText("Booth log unavailable — retrying…")).toBeInTheDocument();
      expect(screen.getByText("Started 'Astral Plane' by Valerie June")).toBeInTheDocument();
    });

    it("surfaces a retryable message when a Load more fetch fails, keeping the button clickable", async () => {
      const headEntries = [makeEntry({ summary: "Head row" })];
      const { state } = installFetchMock({
        head: ok(page(headEntries, "TICKS_1_1")),
        pages: { TICKS_1_1: networkError() },
      });

      render(<BoothLogView timeZone="UTC" />);
      await flush();

      fireEvent.click(screen.getByRole("button", { name: "Load more" }));
      await flush();

      expect(screen.getByText("Couldn’t load more — try again.")).toBeInTheDocument();
      const retryButton = screen.getByRole("button", { name: "Load more" });
      expect(retryButton).not.toBeDisabled();

      state.pages["TICKS_1_1"] = ok(page([makeEntry({ summary: "Recovered row" })], null));
      fireEvent.click(retryButton);
      await flush();

      expect(screen.getByText("Recovered row")).toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: sidebar entry
// ---------------------------------------------------------------------------

describe("Feature: Booth log sidebar entry", () => {
  it("lists Booth log in the persistent sidebar nav", async () => {
    mockedUsePathname.mockReturnValue("/dashboard");

    const { Sidebar } = await import("../app/(authed)/_components/Sidebar");
    render(<Sidebar />);

    expect(screen.getByRole("link", { name: "Booth log" })).toHaveAttribute("href", "/booth-log");
  });
});
