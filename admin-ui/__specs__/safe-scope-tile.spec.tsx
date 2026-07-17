// @jest-environment jsdom
// STORY-132 — The dashboard SafeScope tile tells the truth (Epic U / SPEC F40, closes gitea-#214)
//
// Runner: Jest (jsdom) + @testing-library/react. StatusTiles is a pure, prop-driven presentational
// component (`{ status, error, timeZone }` — no fetch of its own; DashboardView owns polling via
// usePoll, Q5/STORY-087). These specs render it directly with a built `status` prop rather than
// standing up DashboardView's three-endpoint fetch mock (dashboard-page.spec.tsx's style) — the
// tile's own contract is what F40 changed, and rendering it directly is also how the "no new
// fetch" fact (F40.2) gets asserted honestly: a spy on global.fetch that's never called.
//
// U1(c) root-caused gitea-#214 to unlabeled text, not stale scope ids (live GET /api/settings showed a
// single, current, valid override id) — so this file is labeling-only, per F40.1's diagnosis gate.

import { describe, it, expect, jest, afterEach } from "@jest/globals";
import { render, screen, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

interface SafeScopeOverrides {
  libraryIds?: number[];
  playable?: number;
}

/** Catalog/LLM are fixed benign values — these specs exercise the SafeScope tile only. */
function makeStatus(safeScope: SafeScopeOverrides = {}): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: safeScope.libraryIds ?? [7], playable: safeScope.playable ?? 7 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
  };
}

function safeScopeTile(): HTMLElement {
  return screen.getByRole("group", { name: "Safe scope" });
}

afterEach(() => {
  cleanup();
  jest.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: The dashboard SafeScope tile tells the truth
// ---------------------------------------------------------------------------

describe("Feature: The dashboard SafeScope tile tells the truth", () => {
  describe("Scenario: both facts, both labeled", () => {
    it("renders the playable-track count in the headline with the noun — '7 playable tracks' (F40.2)", () => {
      render(<StatusTiles status={makeStatus({ playable: 7, libraryIds: [7] })} error={false} />);

      const tile = safeScopeTile();
      expect(tile).toHaveTextContent("7 playable tracks");
    });

    it("renders the headline noun as singular when playable is 1 — '1 playable track' (gitea-#214 follow-up)", () => {
      render(<StatusTiles status={makeStatus({ playable: 1, libraryIds: [7] })} error={false} />);

      const tile = safeScopeTile();
      expect(tile).toHaveTextContent("1 playable track");
      expect(tile).not.toHaveTextContent("1 playable tracks");
    });

    it("renders the library count with its ids in the sub-line — '2 libraries (ids 2, 3)' (F40.2)", () => {
      render(<StatusTiles status={makeStatus({ playable: 7, libraryIds: [2, 3] })} error={false} />);

      expect(screen.getByText("2 libraries (ids 2, 3)")).toBeInTheDocument();
    });

    it("shows no number anywhere on the tile without a labeling noun (F40.2)", () => {
      render(<StatusTiles status={makeStatus({ playable: 7, libraryIds: [7] })} error={false} />);

      // The pre-fix bug: `Libraries: ${ids.join(", ")}` rendered a bare "Libraries: 7" that read
      // as a library count. Assert that exact shape is gone, on a fixture built to reproduce it
      // (a single-id scope where N and M could otherwise collide).
      const tile = safeScopeTile();
      expect(tile.textContent).not.toMatch(/Libraries:\s*\d/);
      expect(tile.textContent).toContain("1 library (id 7)");
    });

    it("consumes only the existing GET /api/status fields — libraryIds.length is derived client-side (F40.2)", () => {
      const fetchSpy = jest.fn();
      global.fetch = fetchSpy as unknown as typeof fetch;

      render(<StatusTiles status={makeStatus({ playable: 12, libraryIds: [1, 2, 3] })} error={false} />);

      // The sub-line's "3" is the length of the same libraryIds array the headline's playable
      // count rides on — no second endpoint, no separate library-count field.
      expect(screen.getByText("3 libraries (ids 1, 2, 3)")).toBeInTheDocument();
      expect(fetchSpy).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: warning semantics survive the new layout", () => {
    it("applies the F31.5 warning styling when playable is 0 and libraryIds is non-empty (F40.3)", () => {
      render(<StatusTiles status={makeStatus({ playable: 0, libraryIds: [7] })} error={false} />);

      const tile = safeScopeTile();
      expect(tile.className).toMatch(/\bborder-danger\b/);
      expect(screen.getByText("Safe scope has no playable tracks — drains will be silent")).toBeInTheDocument();
    });

    it("keeps the F25.4 empty-scope badge semantics when libraryIds is empty (F40.3)", () => {
      render(<StatusTiles status={makeStatus({ playable: 0, libraryIds: [] })} error={false} />);

      const tile = safeScopeTile();
      expect(screen.getByText("No libraries in scope")).toBeInTheDocument();
      expect(tile.className).not.toMatch(/\bborder-danger\b/);
      expect(
        screen.queryByText("Safe scope has no playable tracks — drains will be silent")
      ).not.toBeInTheDocument();
    });

    it("renders neutral when playable is positive (F40.3)", () => {
      render(<StatusTiles status={makeStatus({ playable: 7, libraryIds: [7] })} error={false} />);

      const tile = safeScopeTile();
      expect(tile.className).toMatch(/\bborder-line\b/);
      expect(tile.className).not.toMatch(/\bborder-danger\b/);
    });
  });

  describe("Scenario (sad path): degraded status data", () => {
    it("renders a skeleton, not a crash, while /api/status has not yet resolved", () => {
      render(<StatusTiles status={null} error={false} />);

      const tile = safeScopeTile();
      expect(screen.getAllByRole("status").length).toBeGreaterThan(0);
      expect(tile).not.toHaveTextContent("playable tracks");
    });

    it("keeps the last rendered counts when a poll fails, per the shipped dashboard behavior", () => {
      // usePoll (lib/use-poll.ts) leaves `data` untouched on a failed poll and only flips
      // `error` — StatusTiles renders the stale status normally rather than reverting to a
      // skeleton or "Unavailable" (that path is `status === null && error`, not this one).
      render(<StatusTiles status={makeStatus({ playable: 7, libraryIds: [7] })} error={true} />);

      const tile = safeScopeTile();
      expect(tile).toHaveTextContent("7 playable tracks");
      expect(tile).toHaveTextContent("1 library (id 7)");
    });
  });
});
