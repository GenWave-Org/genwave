// STORY-248 — Paint the week (SPEC F94.3, PLAN T129), review finding F3: direct model-level
// coverage for `pruneOverrides`, the one rule that decides whether a run's envelope override
// survives a cell mutation. Previously only exercised indirectly through the full editor
// component; this file drives the pure function itself, including the merge-drops-both case the
// component-level specs never painted a scenario for.
//
// Runner: Jest (node) — pure functions, no DOM needed.

import { describe, it, expect } from "@jest/globals";
import {
  createEmptyCells,
  deriveGridFromWeek,
  pruneOverrides,
  runKey,
  serializeWeek,
  type CellValue,
  type ScheduleRun,
  type StoredOverride,
} from "../app/(authed)/schedule/schedule-grid-model";
import type { ScheduleWeekDto } from "../app/(authed)/schedule/types";

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

// ---------------------------------------------------------------------------
// gh-#255 — multi-day / whole-week spans through the serialize ⇄ derive pair.
// The demo repro ladder ("2h across 2 days saves, across 6 days saves, across
// all 7 fails") pinned here at the model level: every span shape must survive
// serializeWeek → (server echo) → deriveGridFromWeek byte-for-byte.
// ---------------------------------------------------------------------------

/** Paints `value` onto `days` × `[startHalfHour, endHalfHour)` on a fresh grid. */
function paintBand(
  cells: CellValue[][],
  days: readonly number[],
  startHalfHour: number,
  endHalfHour: number,
  value: CellValue
): CellValue[][] {
  for (const day of days) {
    for (let h = startHalfHour; h < endHalfHour; h++) {
      const row = cells[day];
      if (row !== undefined) row[h] = value;
    }
  }
  return cells;
}

/** The server's 200 echo: same segments, ids assigned — what `ScheduleController` really does. */
function echo(week: ScheduleWeekDto): ScheduleWeekDto {
  return { segments: week.segments.map((s, i) => ({ ...s, id: i + 1 })) };
}

describe("Feature: multi-day spans serialize and round-trip (gh-#255)", () => {
  const PERSONA = 7;

  it.each([
    ["a 2h band across 2 days", [1, 2]],
    ["a 2h band across 6 days", [1, 2, 3, 4, 5, 6]],
    ["a 2h band across all 7 days", [0, 1, 2, 3, 4, 5, 6]],
  ])("%s serializes one segment per day and round-trips", (_label, days) => {
    const cells = paintBand(createEmptyCells(), days, 20, 24, PERSONA);

    const body = serializeWeek(cells, new Map());

    expect(body.segments).toHaveLength(days.length);
    for (const day of days) {
      expect(body.segments).toContainEqual({
        id: null,
        day,
        startMinute: 600,
        endMinute: 720,
        personaId: PERSONA,
        genres: null,
        energyMin: null,
        energyMax: null,
        showId: null,
      });
    }

    const derived = deriveGridFromWeek(echo(body));
    expect(derived.cells).toEqual(cells);
  });

  it("a whole-week block (all 336 cells, one DJ) serializes as 7 full-day segments and round-trips", () => {
    const cells = paintBand(createEmptyCells(), [0, 1, 2, 3, 4, 5, 6], 0, 48, PERSONA);

    const body = serializeWeek(cells, new Map());

    expect(body.segments).toHaveLength(7);
    for (let day = 0; day < 7; day++) {
      expect(body.segments).toContainEqual(
        expect.objectContaining({ day, startMinute: 0, endMinute: 1440, personaId: PERSONA })
      );
    }

    const derived = deriveGridFromWeek(echo(body));
    expect(derived.cells).toEqual(cells);
  });

  it("a block wrapping the week boundary (Sat 23:00 → Sun 01:00) round-trips as two segments", () => {
    let cells = paintBand(createEmptyCells(), [6], 46, 48, PERSONA);
    cells = paintBand(cells, [0], 0, 2, PERSONA);

    const body = serializeWeek(cells, new Map());

    // Exclusive-end at midnight stays 1440 on Saturday — never collapsed to a zero-length wrap.
    expect(body.segments).toEqual([
      expect.objectContaining({ day: 0, startMinute: 0, endMinute: 60, personaId: PERSONA }),
      expect.objectContaining({ day: 6, startMinute: 1380, endMinute: 1440, personaId: PERSONA }),
    ]);

    const derived = deriveGridFromWeek(echo(body));
    expect(derived.cells).toEqual(cells);
  });

  it("a full-week span keeps a block's envelope override through the round trip", () => {
    const cells = paintBand(createEmptyCells(), [0, 1, 2, 3, 4, 5, 6], 20, 24, PERSONA);
    const overrides = new Map([
      [runKey(3, 20, PERSONA), { end: 24, overrides: { genres: ["jazz"], energyMin: 0.2, energyMax: 0.9 } }],
    ]);

    const body = serializeWeek(cells, overrides);

    expect(body.segments).toContainEqual(
      expect.objectContaining({ day: 3, startMinute: 600, endMinute: 720, genres: ["jazz"], energyMin: 0.2, energyMax: 0.9 })
    );

    const derived = deriveGridFromWeek(echo(body));
    expect(derived.overrides.get(runKey(3, 20, PERSONA))).toEqual({
      end: 24,
      overrides: { genres: ["jazz"], energyMin: 0.2, energyMax: 0.9 },
    });
  });

  // PLAN T243 (B2): showId rides the same runKey-keyed overrides bag genres/energyMin/energyMax
  // already do (deriveGridFromWeek's own hasOverrides/set, serializeWeek's own emit) — pinned here
  // the same way the genres/energy fact just above is. Two rows in the SAME GET document, one with a
  // real showId and one without, so this fails on at least one row if the carry-through is dropped
  // rather than passing by both rows' values coincidentally agreeing.
  it("a week derived from a GET and re-serialized preserves each run's showId", () => {
    const getWeek: ScheduleWeekDto = {
      segments: [
        {
          id: 1,
          day: 3,
          startMinute: 600,
          endMinute: 660,
          personaId: PERSONA,
          genres: null,
          energyMin: null,
          energyMax: null,
          showId: 42,
        },
        {
          id: 2,
          day: 4,
          startMinute: 0,
          endMinute: 60,
          personaId: PERSONA,
          genres: null,
          energyMin: null,
          energyMax: null,
          showId: null,
        },
      ],
    };

    const { cells, overrides } = deriveGridFromWeek(getWeek);
    const resubmitted = serializeWeek(cells, overrides);

    expect(resubmitted.segments).toContainEqual(
      expect.objectContaining({ day: 3, startMinute: 600, endMinute: 660, showId: 42 })
    );
    expect(resubmitted.segments).toContainEqual(
      expect.objectContaining({ day: 4, startMinute: 0, endMinute: 60, showId: null })
    );
  });
});

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
