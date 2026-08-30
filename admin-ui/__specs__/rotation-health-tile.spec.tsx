// @jest-environment jsdom
// STORY-368 / STORY-374 — Dashboard tiles: rotation health and the Gardener queue (SPEC F149.5,
// F153.9-F153.10 · PLAN T371, T378)
//
// BDD specification — jest. The Rotation health scenario is WIRED at T371: StatusTiles renders a
// "Rotation health" tile from the `rotation` block GET /api/status grows (playable, neverAired,
// airedOnce, notAiredDays90, rotationSince — mirrors the file's existing `llm`/`safeScope`
// tile-variant helpers; safe-scope-tile.spec.tsx's own "render StatusTiles directly with a built
// status prop" posture, since the tile is a pure, prop-driven presentational component). The
// Gardener tile (STORY-374 AC8) stays a Jest todo — that is T378's own scope.

import { describe, it, expect, afterEach } from "@jest/globals";
import { render, screen, cleanup } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { StatusTiles } from "../app/(authed)/dashboard/StatusTiles";
import type { StatusResponse } from "@/lib/broadcast-api";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

interface RotationOverrides {
  playable?: number;
  neverAired?: number;
  airedOnce?: number;
  notAiredDays90?: number;
  rotationSince?: string | null;
}

/** Catalog/SafeScope/LLM/Voice are fixed benign values — these specs exercise the Rotation health
 * tile only. `rotation` omitted entirely (not just `undefined`-valued) reproduces an older/absent
 * API response, matching the wire's own optional-field convention. */
function makeStatus(rotation?: RotationOverrides | null): StatusResponse {
  return {
    startedAt: "2026-01-01T08:00:00.000Z",
    catalog: { ready: 10, enriching: 0, failed: 0, unavailable: 0 },
    safeScope: { libraryIds: [1], playable: 1 },
    llm: { enabled: false, model: null, activePersona: null, lastOutcome: null, lastAttemptAt: null },
    voice: { engine: "kokoro", degraded: false, reason: null, checkedAt: null },
    ...(rotation === null
      ? {}
      : {
          rotation: {
            playable: rotation?.playable ?? 10,
            neverAired: rotation?.neverAired ?? 6,
            airedOnce: rotation?.airedOnce ?? 3,
            notAiredDays90: rotation?.notAiredDays90 ?? 1,
            rotationSince: rotation?.rotationSince === undefined ? "2026-08-01T00:00:00.000Z" : rotation.rotationSince,
          },
        }),
  };
}

function rotationTile(): HTMLElement {
  return screen.getByRole("group", { name: "Rotation health" });
}

afterEach(() => {
  cleanup();
});

// ---------------------------------------------------------------------------
// Feature: Dashboard rotation and gardener tiles
// ---------------------------------------------------------------------------

describe("Feature: Dashboard rotation and gardener tiles", () => {
  describe("Scenario: the Rotation health tile", () => {
    it('shows "6 of 10 never aired" for status.rotation neverAired 6 of 10 playable (STORY-368 AC2)', () => {
      render(<StatusTiles status={makeStatus()} error={false} timeZone="UTC" />);

      expect(rotationTile()).toHaveTextContent("6 of 10 never aired");
    });

    it("shows the aired-once and stale (90 d) counts on the secondary line (STORY-368 AC2)", () => {
      render(<StatusTiles status={makeStatus()} error={false} timeZone="UTC" />);

      expect(rotationTile()).toHaveTextContent("3 aired once · 1 stale (90 d)");
    });

    it("shows the ledger epoch (rotationSince) beside the count, sentence-cased (STORY-368 AC2, Dean's capital rule)", () => {
      render(<StatusTiles status={makeStatus()} error={false} timeZone="UTC" />);

      expect(rotationTile()).toHaveTextContent("Since Aug 1, 2026");
    });

    it("omits the epoch line when rotationSince is null (a pre-Gardener install)", () => {
      render(<StatusTiles status={makeStatus({ rotationSince: null })} error={false} timeZone="UTC" />);

      expect(rotationTile()).not.toHaveTextContent("Since");
    });

    it("applies the warning styling and names the reason once never-aired exceeds half the playable pool (T371 review MED-3)", () => {
      render(
        <StatusTiles status={makeStatus({ playable: 10, neverAired: 6 })} error={false} timeZone="UTC" />
      );

      const tile = rotationTile();
      expect(tile.className).toMatch(/\bborder-danger\b/);
      expect(tile).toHaveTextContent("More than half the playable catalog has never aired");
    });

    it("stays neutral (and names no reason) when never-aired is at most half the playable pool", () => {
      render(
        <StatusTiles status={makeStatus({ playable: 10, neverAired: 5 })} error={false} timeZone="UTC" />
      );

      const tile = rotationTile();
      expect(tile.className).not.toMatch(/\bborder-danger\b/);
      expect(tile).not.toHaveTextContent("More than half the playable catalog has never aired");
    });

    it("renders Unavailable when the rotation block is absent from the response", () => {
      render(<StatusTiles status={makeStatus(null)} error={false} timeZone="UTC" />);

      expect(rotationTile()).toHaveTextContent("Unavailable");
    });
  });

  describe("Scenario: the Gardener tile", () => {
    it.todo("the tile shows the open finding count for each kind from status.gardener (pending T378, STORY-374 AC8)");
  });
});
