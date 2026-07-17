// @jest-environment jsdom
// STORY-105 — Depleted SafeScope visible at rest (Epic R / SPEC F31.4–F31.5, gitea-#186)
//
// Two surfaces read the same GET /api/status aggregate (`safeScope.playable`, the exact
// /internal/safe-track predicate — no new endpoint): the settings SafeScope picker's
// SafeScopeAvailabilityBadge (mounted only for the SafeScope field; mount-fetches, then
// polls at the shared 5 s cadence — lib/use-poll.ts) and the dashboard's SafeScope tile
// (already polling via usePoll, Q5/STORY-087). Fetch mocking mirrors the two closest house
// patterns: safe-scope-empty-badge.spec.tsx's single-response SettingsForm mock for the
// picker, and dashboard-page.spec.tsx's per-endpoint dispatching mock under fake timers for
// the tile.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, act, waitFor, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactElement } from "react";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { SettingsForm } from "../app/(authed)/settings/SettingsForm";
import type { SettingDto } from "../app/(authed)/settings/SettingsForm";
import type { LibraryDto } from "../lib/library";
import { DashboardView } from "../app/(authed)/dashboard/DashboardView";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SAFE_SCOPE_KEY = "Station:SafeScope:LibraryIds";
const DEPLETED_COPY = /no playable tracks — drains will be silent/i;
const EMPTY_SCOPE_COPY = /silent on drain/i;
const ISO_NOW = "2026-01-01T12:00:00.000Z";
const RAW_PALETTE_CLASS = /\b(?:bg|text|border)-(?:red|orange|amber|yellow|rose)-\d{2,3}\b/;
const RAW_HEX_CLASS = /#[0-9a-fA-F]{3,6}\b/;

function makeSafeScopeSetting(override: Partial<SettingDto> = {}): SettingDto {
  return {
    key: SAFE_SCOPE_KEY,
    value: "[1]",
    source: "override",
    applyMode: "live",
    kind: "number-list",
    unit: "",
    ...override,
  };
}

function makeLibraries(): LibraryDto[] {
  return [
    { id: 1, name: "Lib Alpha", mediaCount: 10 },
    { id: 2, name: "Lib Beta", mediaCount: 5 },
  ];
}

interface StatusBodyOverrides {
  libraryIds?: number[];
  playable?: number;
}

function makeStatusBody(overrides: StatusBodyOverrides = {}): unknown {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: overrides.libraryIds ?? [1], playable: overrides.playable ?? 0 },
    // STORY-125: disabled/never-attempted — this file's scenarios exercise the SafeScope tile only.
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
  };
}

/**
 * SettingsForm calls useConfirm() unconditionally, so every render needs a
 * ConfirmDialogProvider ancestor (matches safe-scope-empty-badge.spec.tsx).
 */
function renderWithProviders(node: ReactElement): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      {node}
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** A fetch mock returning the same GET /api/status body to every call (settings-page.spec.tsx's makeFetchMock pattern). */
function makeStatusFetchMock(body: unknown, status = 200): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

// ---------------------------------------------------------------------------
// Dashboard-side fetch mock — dashboard-page.spec.tsx's per-endpoint dispatch, pared to
// what DashboardView's three pollers need. now-playing/history are fixed to benign values
// (these specs are only exercising the SafeScope tile); status is the mutable part.
// ---------------------------------------------------------------------------

interface DashboardFetchState {
  status: unknown;
}

function endpointKeyFor(url: string): "now" | "status" | "history" {
  if (url.includes("now-playing")) return "now";
  if (url.includes("play-history")) return "history";
  return "status";
}

function installDashboardFetchMock(initialStatus: unknown): {
  fn: jest.MockedFunction<typeof fetch>;
  state: DashboardFetchState;
} {
  const state: DashboardFetchState = { status: initialStatus };
  const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
    const key = endpointKeyFor(String(input));
    if (key === "now") {
      return Promise.resolve({ ok: false, status: 503, json: () => Promise.resolve({}) } as Response);
    }
    if (key === "history") {
      return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve([]) } as Response);
    }
    return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(state.status) } as Response);
  });
  global.fetch = fn as unknown as typeof fetch;
  return { fn, state };
}

/** Flushes the initial-mount poll without advancing fake time (dashboard-page.spec.tsx's helper). */
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

// ---------------------------------------------------------------------------
// Feature: Depleted SafeScope visible at rest
// ---------------------------------------------------------------------------

describe("Feature: Depleted SafeScope visible at rest", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // -------------------------------------------------------------------------
  describe("Scenario: SafeScope non-empty with zero playable tracks", () => {
    it("shows the 'Safe scope has no playable tracks — drains will be silent' badge on the settings SafeScope picker", async () => {
      makeStatusFetchMock(makeStatusBody({ libraryIds: [1], playable: 0 }));

      renderWithProviders(
        <SettingsForm settings={[makeSafeScopeSetting({ value: "[1]" })]} libraries={makeLibraries()} />
      );

      await waitFor(() => {
        expect(screen.getByText(DEPLETED_COPY)).toBeInTheDocument();
      });
    });

    it("renders the dashboard SafeScope tile in its warning state", async () => {
      jest.useFakeTimers({ now: new Date(ISO_NOW) });
      try {
        installDashboardFetchMock(makeStatusBody({ libraryIds: [1, 7], playable: 0 }));
        render(<DashboardView timeZone="UTC" />);
        await flush();

        const tile = screen.getByRole("group", { name: "Safe scope" });
        expect(tile.className).toMatch(/\bborder-danger\b/);
        expect(screen.getByText(DEPLETED_COPY)).toBeInTheDocument();
      } finally {
        jest.useRealTimers();
      }
    });

    it("uses token-layer warning styling (no raw palette classes)", async () => {
      makeStatusFetchMock(makeStatusBody({ libraryIds: [1], playable: 0 }));
      renderWithProviders(
        <SettingsForm settings={[makeSafeScopeSetting({ value: "[1]" })]} libraries={makeLibraries()} />
      );
      const badge = await screen.findByText(DEPLETED_COPY);
      expect(badge.className).toMatch(/\btext-danger\b/);
      expect(badge.className).toMatch(/\bborder-danger\b/);
      expect(badge.className).not.toMatch(RAW_HEX_CLASS);
      expect(badge.className).not.toMatch(RAW_PALETTE_CLASS);
      cleanup();

      jest.useFakeTimers({ now: new Date(ISO_NOW) });
      try {
        installDashboardFetchMock(makeStatusBody({ libraryIds: [1, 7], playable: 0 }));
        render(<DashboardView timeZone="UTC" />);
        await flush();

        const tile = screen.getByRole("group", { name: "Safe scope" });
        expect(tile.className).toMatch(/\bborder-danger\b/);
        expect(tile.className).not.toMatch(RAW_HEX_CLASS);
        expect(tile.className).not.toMatch(RAW_PALETTE_CLASS);
      } finally {
        jest.useRealTimers();
      }
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: SafeScope has playable tracks", () => {
    it("shows no depleted badge on the settings picker", async () => {
      const mockFetch = makeStatusFetchMock(makeStatusBody({ libraryIds: [1], playable: 12 }));

      renderWithProviders(
        <SettingsForm settings={[makeSafeScopeSetting({ value: "[1]" })]} libraries={makeLibraries()} />
      );

      // Confirm the mount-fetch round-trip actually settled before asserting the negative,
      // so this isn't trivially true just because the poll hasn't resolved yet.
      await waitFor(() => expect(mockFetch).toHaveBeenCalled());
      expect(screen.queryByText(DEPLETED_COPY)).not.toBeInTheDocument();
    });

    it("renders the dashboard tile in its neutral state", async () => {
      jest.useFakeTimers({ now: new Date(ISO_NOW) });
      try {
        installDashboardFetchMock(makeStatusBody({ libraryIds: [1, 7], playable: 45 }));
        render(<DashboardView timeZone="UTC" />);
        await flush();

        const tile = screen.getByRole("group", { name: "Safe scope" });
        expect(tile.className).toMatch(/\bborder-line\b/);
        expect(tile.className).not.toMatch(/\bborder-danger\b/);
        expect(screen.queryByText(DEPLETED_COPY)).not.toBeInTheDocument();
      } finally {
        jest.useRealTimers();
      }
    });
  });

  // -------------------------------------------------------------------------
  describe("Scenario: the state changes between polls", () => {
    it("shows the warning within one poll interval after playable drops to zero", async () => {
      jest.useFakeTimers({ now: new Date(ISO_NOW) });
      try {
        const { state } = installDashboardFetchMock(makeStatusBody({ libraryIds: [1, 7], playable: 12 }));
        render(<DashboardView timeZone="UTC" />);
        await flush();

        expect(screen.queryByText(DEPLETED_COPY)).not.toBeInTheDocument();

        state.status = makeStatusBody({ libraryIds: [1, 7], playable: 0 });
        await advance(5000);

        const tile = screen.getByRole("group", { name: "Safe scope" });
        expect(tile.className).toMatch(/\bborder-danger\b/);
        expect(screen.getByText(DEPLETED_COPY)).toBeInTheDocument();
      } finally {
        jest.useRealTimers();
      }
    });
  });

  // -------------------------------------------------------------------------
  // SAD PATH
  // -------------------------------------------------------------------------

  describe("Scenario: empty scope keeps its own badge (sad path)", () => {
    it("shows the F25.4 'Silent on drain' badge — not the depleted-scope copy — when SafeScope is []", async () => {
      makeStatusFetchMock(makeStatusBody({ libraryIds: [], playable: 0 }));

      renderWithProviders(
        <SettingsForm settings={[makeSafeScopeSetting({ value: "[]" })]} libraries={makeLibraries()} />
      );

      expect(screen.getByText(EMPTY_SCOPE_COPY)).toBeInTheDocument();
      expect(screen.queryByText(DEPLETED_COPY)).not.toBeInTheDocument();

      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });
    });

    it("never shows both badges at once", async () => {
      // Effective SafeScope is [] (F25.4's config-level territory) while /api/status also
      // happens to report playable === 0 — a real depleted signal for the *effective* scope
      // is moot once that scope is empty. Empty wins regardless of what the poll reports.
      const mockFetch = makeStatusFetchMock(makeStatusBody({ libraryIds: [], playable: 0 }));

      renderWithProviders(
        <SettingsForm settings={[makeSafeScopeSetting({ value: "[]" })]} libraries={makeLibraries()} />
      );

      // Let the mount-fetch settle so a would-be depleted badge has had its chance to render.
      await waitFor(() => expect(mockFetch).toHaveBeenCalled());
      await act(async () => {
        await Promise.resolve();
        await Promise.resolve();
      });

      expect(screen.getByText(EMPTY_SCOPE_COPY)).toBeInTheDocument();
      expect(screen.queryByText(DEPLETED_COPY)).not.toBeInTheDocument();
    });
  });
});
