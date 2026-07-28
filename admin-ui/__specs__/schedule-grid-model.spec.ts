// STORY-248 — Paint the week (SPEC F94.3, PLAN T129), review finding F3: direct model-level
// coverage for `pruneOverrides`, the one rule that decides whether a run's envelope override
// survives a cell mutation. Previously only exercised indirectly through the full editor
// component; this file drives the pure function itself, including the merge-drops-both case the
// component-level specs never painted a scenario for.
//
// Runner: Jest (node) — pure functions, no DOM needed.

import { describe, it, expect } from "@jest/globals";
import { pruneOverrides, runKey, type ScheduleRun, type StoredOverride } from "../app/(authed)/schedule/schedule-grid-model";

const GENRES_A = { genres: ["rock"], energyMin: null, energyMax: null };
const GENRES_B = { genres: ["pop"], energyMin: null, energyMax: null };

function run(day: number, start: number, end: number, brush: ScheduleRun["brush"] = 1): ScheduleRun {
  return { day, start, end, brush };
}

function overridesOf(entries: ReadonlyArray<readonly [ScheduleRun, StoredOverride["overrides"]]>): Map<string, StoredOverride> {
  const map = new Map<string, StoredOverride>();
  for (const [r, overrides] of entries) {
    map.set(runKey(r.day, r.start, r.brush), { end: r.end, overrides });
  }
  return map;
}

describe("Feature: pruneOverrides reconciles the overrides map against the live run set", () => {
  describe("Scenario: extending a run forward into an empty gap keeps its override", () => {
    it("keeps the override under the same key, with the end updated to the wider run", () => {
      const before = run(0, 0, 5);
      const after = run(0, 0, 8); // grew forward into cells 5-7, which held nothing before.
      const overrides = overridesOf([[before, GENRES_A]]);

      const next = pruneOverrides(overrides, [after]);

      expect(next.get(runKey(0, 0, 1))).toEqual({ end: 8, overrides: GENRES_A });
    });
  });

  describe("Scenario: extending a run backward drops its override", () => {
    it("drops the override — its key's start no longer matches any live run", () => {
      const before = run(0, 5, 10);
      const after = run(0, 0, 10); // grew backward: the run's start moved from 5 to 0.
      const overrides = overridesOf([[before, GENRES_A]]);

      const next = pruneOverrides(overrides, [after]);

      expect(next.size).toBe(0);
    });
  });

  describe("Scenario: splitting a run keeps the override only on the leftmost piece", () => {
    const original = run(0, 0, 10);
    const leftPiece = run(0, 0, 4); // brush 1, unchanged start.
    const middlePiece = run(0, 4, 6, 2); // a different brush painted into the middle.
    const rightPiece = run(0, 6, 10); // brush 1 again, but a NEW start — not the original run's key.

    it("keeps the leftmost piece's override, with its end shrunk to the narrower span", () => {
      const overrides = overridesOf([[original, GENRES_A]]);

      const next = pruneOverrides(overrides, [leftPiece, middlePiece, rightPiece]);

      expect(next.get(runKey(0, 0, 1))).toEqual({ end: 4, overrides: GENRES_A });
    });

    it("leaves the other split-created pieces with no override at all", () => {
      const overrides = overridesOf([[original, GENRES_A]]);

      const next = pruneOverrides(overrides, [leftPiece, middlePiece, rightPiece]);

      expect(next.size).toBe(1); // only the leftmost key above — nothing for middle/right.
    });
  });

  describe("Scenario: erasing a run drops its override for good", () => {
    it("drops the override when the run vanishes entirely", () => {
      const before = run(0, 0, 5);
      const overrides = overridesOf([[before, GENRES_A]]);

      const next = pruneOverrides(overrides, []); // the cells were cleared — no live runs at all.

      expect(next.size).toBe(0);
    });

    it("does not silently reattach to a later, separate run recreated at the identical slot", () => {
      const before = run(0, 0, 5);
      const overrides = overridesOf([[before, GENRES_A]]);
      const erased = pruneOverrides(overrides, []); // first mutation: erase.
      const recreated = run(0, 0, 5); // second, SEPARATE mutation: repaint the identical block.

      const next = pruneOverrides(erased, [recreated]);

      expect(next.size).toBe(0);
    });
  });

  describe("Scenario: merging two separately-overridden same-brush runs drops both (reviewer ruling)", () => {
    it("drops the surviving run's own override, not just the absorbed run's", () => {
      const first = run(0, 0, 5); // override GENRES_A
      const second = run(0, 7, 10); // override GENRES_B, separated from `first` by a gap.
      const merged = run(0, 0, 10); // the gap (5-7) got painted, joining both into one run.
      const overrides = overridesOf([
        [first, GENRES_A],
        [second, GENRES_B],
      ]);

      const next = pruneOverrides(overrides, [merged]);

      expect(next.get(runKey(0, 0, 1))).toBeUndefined();
    });

    it("drops the absorbed run's override too — the merged run gets station defaults, not either side's", () => {
      const first = run(0, 0, 5);
      const second = run(0, 7, 10);
      const merged = run(0, 0, 10);
      const overrides = overridesOf([
        [first, GENRES_A],
        [second, GENRES_B],
      ]);

      const next = pruneOverrides(overrides, [merged]);

      expect(next.size).toBe(0);
    });

    it("does not confuse a merge with a plain forward-extend into an unoverridden run", () => {
      const first = run(0, 0, 5); // the only override in play.
      const second = run(0, 7, 10); // no override of its own.
      const merged = run(0, 0, 10);
      const overrides = overridesOf([[first, GENRES_A]]);

      const next = pruneOverrides(overrides, [merged]);

      // Nothing else was absorbed — `first`'s override survives the extension untouched.
      expect(next.get(runKey(0, 0, 1))).toEqual({ end: 10, overrides: GENRES_A });
    });
  });
});
