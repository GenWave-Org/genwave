// STORY-368 / STORY-374 — Dashboard tiles: rotation health and the Gardener queue (SPEC F149.5,
// F153.9-F153.10 · PLAN T371, T378)
//
// BDD specification — jest. PENDING until T371/T378. T371 adds a Rotation health tile to
// app/(authed)/dashboard/StatusTiles.tsx, reading the new `rotation` block GET /api/status grows
// (neverAired, airedOnce, notAiredDays90, rotationSince — mirrors the file's existing `llm`/
// `safeScope` tile-variant helpers). T378 adds a Gardener tile beside it on the same page, reading
// a new `gardener` block on the same GET /api/status response (open finding counts per kind).

import { describe, it } from "@jest/globals";

describe("Feature: Dashboard rotation and gardener tiles", () => {
  describe("Scenario: the Rotation health tile", () => {
    it.todo(
      'given status.rotation neverAired 6 of 10 playable, the tile shows "never aired 6 of 10" (pending T371, STORY-368 AC2)'
    );
    it.todo("the tile shows the ledger epoch (rotationSince) beside the count (pending T371, STORY-368 AC2)");
  });

  describe("Scenario: the Gardener tile", () => {
    it.todo("the tile shows the open finding count for each kind from status.gardener (pending T378, STORY-374 AC8)");
  });
});
