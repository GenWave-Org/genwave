// @jest-environment jsdom
// gh-#148 — Admin UI: Health page — container-level view of the running stack
// gh-#490 — restart count paired with recency (a historical restart storm must not read as live)
//
// Runner: Jest (jsdom) + @testing-library/react. Drives HealthView (the client component the
// Health page renders) with a mocked global.fetch — mirrors llm-calls-page.spec.tsx's
// installFetchMock style (one endpoint, no paging). Fake timers pinned to ISO_NOW throughout —
// the 12s poll cadence and the gh-#490 restart-recency math both need a fixed "now". The
// chip/formatting helpers (stateChip, formatBytes) are covered directly at the bottom — pure
// functions, no render needed.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, within, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { HealthView, stateChip, formatBytes } from "../app/(authed)/health/HealthView";
import type { ContainerStat, ContainerStatsReport } from "@/lib/container-stats-api";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

// Anchors every "ago" computation below — the demo-box case gh-#490 describes: a restart storm
// on 2026-08-09 read against a "now" of 2026-08-13 (4 days later).
const ISO_NOW = "2026-08-13T12:00:00.000Z";

function makeContainer(overrides: Partial<ContainerStat> = {}): ContainerStat {
  return {
    name: "api",
    state: "running",
    health: "healthy",
    cpuPercent: 3.2,
    memoryUsedBytes: 400 * 1024 * 1024,
    memoryLimitBytes: 3 * 1024 * 1024 * 1024,
    restartCount: 0,
    startedAt: null,
    ...overrides,
  };
}

function makeReport(containers: ContainerStat[]): ContainerStatsReport {
  return { degraded: false, reason: null, containers };
}

function degradedReport(reason: string | null): ContainerStatsReport {
  return { degraded: true, reason, containers: [] };
}

type MockResult = { kind: "ok"; body: unknown } | { kind: "network-error" };

function ok(body: unknown): MockResult {
  return { kind: "ok", body };
}

function networkError(): MockResult {
  return { kind: "network-error" };
}

function installFetchMock(initial: MockResult) {
  const state = { current: initial };
  const fn = jest.fn<typeof fetch>().mockImplementation(() => {
    const result = state.current;
    if (result.kind === "network-error") {
      return Promise.reject(new Error("network error"));
    }
    return Promise.resolve({
      ok: true,
      status: 200,
      json: () => Promise.resolve(result.body),
    } as Response);
  });
  global.fetch = fn as unknown as typeof fetch;
  return { fn, state };
}

/** Flushes the initial-mount poll without advancing fake time. */
async function flush(): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(0);
  });
}

/** Advances fake time and flushes the resulting fetch/json promise chain. */
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
// Specs
// ---------------------------------------------------------------------------

describe("Feature: Health page container cards (gh-#148)", () => {
  describe("Scenario: a healthy stack renders one card per service", () => {
    it("renders name, state chip, cpu, and memory for each container", async () => {
      installFetchMock(
        ok(
          makeReport([
            makeContainer({ name: "api", cpuPercent: 3.2 }),
            makeContainer({ name: "engine", cpuPercent: 12.7, health: null }),
          ])
        )
      );
      render(<HealthView />);
      await flush();

      const apiCard = screen.getByRole("group", { name: "api" });
      expect(within(apiCard).getByText("running")).toBeInTheDocument();
      expect(within(apiCard).getByText("3.2%")).toBeInTheDocument();
      expect(within(apiCard).getByText("400 MiB / 3.0 GiB")).toBeInTheDocument();

      const engineCard = screen.getByRole("group", { name: "engine" });
      expect(within(engineCard).getByText("12.7%")).toBeInTheDocument();
    });

    it("renders an unknown cpu as a dash, never a fabricated zero", async () => {
      installFetchMock(ok(makeReport([makeContainer({ name: "db", cpuPercent: null })])));
      render(<HealthView />);
      await flush();

      const card = screen.getByRole("group", { name: "db" });
      expect(within(card).getByText("—")).toBeInTheDocument();
      expect(within(card).queryByText("0.0%")).not.toBeInTheDocument();
    });

    it("hides the restart line entirely for a zero count — today's behavior, unchanged", async () => {
      installFetchMock(ok(makeReport([makeContainer({ name: "api", restartCount: 0, startedAt: null })])));
      render(<HealthView />);
      await flush();

      expect(within(screen.getByRole("group", { name: "api" })).queryByText(/restart/)).not.toBeInTheDocument();
    });
  });

  describe("Scenario: a restart count pairs with recency, not a bare number (gh-#490)", () => {
    it("a restart inside the last 24h reads as a live alarm, with the recency text", async () => {
      // Given a restart 1 hour before "now" — well inside the recent window
      installFetchMock(
        ok(
          makeReport([
            makeContainer({ name: "kokoro", restartCount: 3, startedAt: "2026-08-13T11:00:00.000Z" }),
          ])
        )
      );
      render(<HealthView />);
      await flush();

      const restartLine = within(screen.getByRole("group", { name: "kokoro" })).getByText(
        "3 restarts · last 1h ago"
      );
      expect(restartLine).toBeInTheDocument();
      expect(restartLine).toHaveClass("text-danger");
    });

    it("a restart days old reads muted, with the recency text — never a live alarm", async () => {
      // Given the gh-#490 demo-box case: restarts=10, last restart 2026-08-09 against "now" 08-13
      installFetchMock(
        ok(
          makeReport([
            makeContainer({ name: "db", restartCount: 10, startedAt: "2026-08-09T08:00:00.000Z" }),
          ])
        )
      );
      render(<HealthView />);
      await flush();

      const restartLine = within(screen.getByRole("group", { name: "db" })).getByText("10 restarts · last 4d ago");
      expect(restartLine).toBeInTheDocument();
      expect(restartLine).toHaveClass("text-mute");
      expect(restartLine).not.toHaveClass("text-danger");
    });

    it("zero restarts renders no restart line at all, regardless of startedAt", async () => {
      installFetchMock(
        ok(makeReport([makeContainer({ name: "api", restartCount: 0, startedAt: "2026-08-09T08:00:00.000Z" })]))
      );
      render(<HealthView />);
      await flush();

      expect(within(screen.getByRole("group", { name: "api" })).queryByText(/restart/)).not.toBeInTheDocument();
    });
  });

  describe("Scenario: state chips fold the health verdict into the lifecycle state", () => {
    it("an unhealthy running container reads unhealthy, never a green running", async () => {
      installFetchMock(ok(makeReport([makeContainer({ name: "icecast", state: "running", health: "unhealthy" })])));
      render(<HealthView />);
      await flush();

      const card = screen.getByRole("group", { name: "icecast" });
      expect(within(card).getByText("unhealthy")).toBeInTheDocument();
      expect(within(card).queryByText("running")).not.toBeInTheDocument();
    });

    it("an exited container still renders its card with a quiet chip", async () => {
      installFetchMock(
        ok(makeReport([makeContainer({ name: "piper", state: "exited", health: null, cpuPercent: null, memoryUsedBytes: null, memoryLimitBytes: null })]))
      );
      render(<HealthView />);
      await flush();

      expect(within(screen.getByRole("group", { name: "piper" })).getByText("exited")).toBeInTheDocument();
    });
  });

  describe("Scenario: degraded and error states render 'stats unavailable', never an error page", () => {
    it("a degraded report shows the api's own reason and no cards", async () => {
      installFetchMock(ok(degradedReport("Container stats sidecar unreachable at http://dockerproxy:2375.")));
      render(<HealthView />);
      await flush();

      expect(screen.getByText("Container stats unavailable")).toBeInTheDocument();
      expect(screen.getByText("Container stats sidecar unreachable at http://dockerproxy:2375.")).toBeInTheDocument();
      expect(screen.queryByRole("group", { name: "api" })).not.toBeInTheDocument();
    });

    it("a poll failure before any data shows the unavailable card", async () => {
      installFetchMock(networkError());
      render(<HealthView />);
      await flush();

      expect(screen.getByText("Container stats unavailable")).toBeInTheDocument();
    });

    it("a poll failure after data keeps the stale cards with a quiet retrying hint", async () => {
      const { state } = installFetchMock(ok(makeReport([makeContainer({ name: "api" })])));
      render(<HealthView />);
      await flush();
      expect(screen.getByRole("group", { name: "api" })).toBeInTheDocument();

      state.current = networkError();
      await advance(12_000);

      expect(screen.getByRole("group", { name: "api" })).toBeInTheDocument();
      expect(screen.getByText(/retrying/)).toBeInTheDocument();
    });
  });

  describe("Scenario: the page polls on the 12s cadence", () => {
    it("fetches on mount and again after each interval, updating in place", async () => {
      const { fn, state } = installFetchMock(ok(makeReport([makeContainer({ name: "api", cpuPercent: 3.2 })])));
      render(<HealthView />);
      await flush();
      expect(fn).toHaveBeenCalledTimes(1);
      expect(screen.getByText("3.2%")).toBeInTheDocument();

      state.current = ok(makeReport([makeContainer({ name: "api", cpuPercent: 47.9 })]));
      await advance(12_000);

      expect(fn).toHaveBeenCalledTimes(2);
      expect(screen.getByText("47.9%")).toBeInTheDocument();
      expect(screen.queryByText("3.2%")).not.toBeInTheDocument();
    });
  });
});

describe("Feature: chip and byte-formatting helpers (gh-#148)", () => {
  it("stateChip maps the verdict-folded vocabulary", () => {
    expect(stateChip({ state: "running", health: "healthy" })).toEqual({ label: "running", variant: "ok" });
    expect(stateChip({ state: "running", health: null })).toEqual({ label: "running", variant: "ok" });
    expect(stateChip({ state: "running", health: "unhealthy" })).toEqual({ label: "unhealthy", variant: "warning" });
    expect(stateChip({ state: "running", health: "starting" })).toEqual({ label: "starting", variant: "muted" });
    expect(stateChip({ state: "restarting", health: null })).toEqual({ label: "restarting", variant: "warning" });
    expect(stateChip({ state: "dead", health: null })).toEqual({ label: "dead", variant: "warning" });
    expect(stateChip({ state: "exited", health: null })).toEqual({ label: "exited", variant: "muted" });
    expect(stateChip({ state: "some-future-state", health: null })).toEqual({
      label: "some-future-state",
      variant: "muted",
    });
  });

  it("formatBytes uses docker's 1024-based units", () => {
    expect(formatBytes(400 * 1024 * 1024)).toBe("400 MiB");
    expect(formatBytes(3 * 1024 * 1024 * 1024)).toBe("3.0 GiB");
    expect(formatBytes(512 * 1024)).toBe("512 KiB");
  });
});
