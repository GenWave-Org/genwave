import type { RosterPersonaDto, ScheduleCellErrorDto, ScheduleSegmentDto, ScheduleWeekDto } from "./types";

/**
 * The T129 paint model, in one pure module (no React, no fetch) — STORY-248, SPEC F94.3, the
 * "or bust" deliverable's state/serialization core. Every function here is a plain data transform
 * so the grid's pointer plumbing (`ScheduleGrid.tsx`) and orchestration (`ScheduleEditor.tsx`) stay
 * thin wrappers around it.
 *
 * ── The grid ──────────────────────────────────────────────────────────────────────────────────
 * `cells[day][halfHour]` is the entire local editing state for the WEEK GRID: `day` 0-6
 * (Sunday=0, matching the wire), `halfHour` 0-47 (each cell = 30 minutes, 00:00-24:00). A cell
 * holds a {@link PaintedValue} (a persona id, or `"music"` for the music-only brush) or `null` for
 * a gap (no segment at all, SPEC F91.4).
 *
 * ── Runs, not cells, are what gets saved ─────────────────────────────────────────────────────
 * The wire only knows about `segment`s: contiguous same-brush runs per day. {@link computeRuns}
 * derives them fresh from the cell grid every time — there is no separately-tracked "segment"
 * identity independent of the cells. This is what makes "painting over existing cells REPLACES
 * them" (the ruled model) trivial: a cell write is just an array assignment, and the run structure
 * falls out of whatever's contiguous afterward.
 *
 * ── Envelope overrides, keyed by run identity ────────────────────────────────────────────────
 * A run's optional genre/energy override — and, since PLAN T243, its show id — is stored in a
 * `Map<string, StoredOverride>` keyed by
 * {@link runKey} = `day:start:brush` (see that function's own remarks for exactly why `brush` is
 * part of the key, not just `day:start`). Each stored entry also carries the run's `end` AS OF
 * the mutation that last touched it — the one extra fact {@link pruneOverrides} needs to tell a
 * benign forward-extend apart from a merge that swallows a second overridden run (case 5 below).
 * {@link pruneOverrides} is called after every cell mutation with the FRESH run set that mutation
 * produced, and mechanically implements every documented override-survival/drop case:
 *   1. Extending a run FORWARD into an EMPTY gap (painting more of the same brush after its
 *      current end, where nothing else used to live there) keeps the run's `day:start`
 *      unchanged → the override survives, its stored `end` updated to the new, wider value.
 *   2. Extending a run BACKWARD (prepending earlier same-brush cells) moves its `start` → the old
 *      key vanishes → the override is dropped. The operator re-enters it for the new, wider block.
 *   3. Splitting a run (painting a different brush into its middle) leaves only the LEFTMOST
 *      resulting piece with the original `day:start` → it alone keeps the override (its `end`
 *      shrinks to the new, narrower value); every other piece the split created starts blank
 *      (this is the literal "splitting a run drops overrides for the new pieces" rule STORY-248
 *      asks for).
 *   4. Erasing a run (the `clear` brush) removes its key from the current-run set outright → the
 *      override is gone for good, even if a LATER, separate paint stroke recreates an identical
 *      `day:start:brush` run — pruning already deleted the map entry, so there is nothing left to
 *      coincidentally reattach. This is the conservative half of the rule: an override is never
 *      silently applied to a block the operator didn't set it on, even one that happens to occupy
 *      the same slot a deleted block used to.
 *   5. Merging two SEPARATELY-overridden same-brush runs (painting the gap between them, joining
 *      them into one contiguous run) drops BOTH overrides — reviewer ruling, STORY-248: applying
 *      either run's envelope across the other's former cells would silently discard information
 *      the operator entered on purpose, so the merged run gets station defaults and the operator
 *      re-opens the panel deliberately. Mechanically: the surviving run's `day:start` key matches
 *      an old entry (case 1's shape exactly), but its span now reaches into cells that, per a
 *      DIFFERENT old override's own `day:start`, used to belong to a separate run — that is what
 *      distinguishes "grew into a blank gap" (case 1, keep) from "grew by absorbing another
 *      overridden run" (case 5, drop both) when the run's `day:start` alone can't tell them apart.
 */

export const DAY_COUNT = 7;
export const HALF_HOURS_PER_DAY = 48;
export const MINUTES_PER_HALF_HOUR = 30;

/** Sunday=0, matching the wire's own `day` numbering (`System.DayOfWeek`). */
export const DAY_LABELS: readonly string[] = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

/** Same ordering as {@link DAY_LABELS}, spelled out — used for cell `aria-label`s where the
 * abbreviation the day-header column uses would be too terse read out of context. */
export const DAY_FULL_NAMES: readonly string[] = [
  "Sunday",
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
];

/** A cell's paint: a persona id, or the sentinel for the music-only brush (a REAL segment with no
 * DJ — never confused with a gap). */
export type PaintedValue = number | "music";

/** A grid cell's value: painted, or `null` for a gap (no segment covers this half-hour). */
export type CellValue = PaintedValue | null;

/** One roster palette entry. `clear` is deliberately NOT called "eraser" in this type even though
 * that's its UI affordance — it paints `null` (a real, explicit "no coverage here" write), the
 * same as any other brush paints its own value; it isn't a distinct code path. */
export type Brush =
  | { kind: "persona"; personaId: number; name: string }
  | { kind: "music" }
  | { kind: "clear" };

export interface BlockOverrides {
  genres: string[] | null;
  energyMin: number | null;
  energyMax: number | null;
  /** T243: carried in this same bag, the same way as `genres`/`energyMin`/`energyMax` — see this
   * module's own remarks on `deriveGridFromWeek`/`serializeWeek` for exactly how it rides the
   * `runKey`-keyed overrides map (and therefore inherits {@link pruneOverrides}' own
   * survives/drops rules on a repaint — merged-run disagreement is T245's picker's own concern, not
   * this bag's). */
  showId: number | null;
}

/** An override entry as stored in the overrides map — the {@link BlockOverrides} the operator set,
 * plus the run's `end` as of the mutation that last wrote this entry. `end` is the merge-detection
 * anchor {@link pruneOverrides} needs (see this module's own doc comment, case 5): it is never read
 * for any other purpose. */
export interface StoredOverride {
  end: number;
  overrides: BlockOverrides;
}

/** The overrides map's own type — `Map<string, StoredOverride>`, keyed by {@link runKey}. */
export type OverridesMap = Map<string, StoredOverride>;

const NO_OVERRIDES: BlockOverrides = { genres: null, energyMin: null, energyMax: null, showId: null };

/** A contiguous same-brush run within one day — the unit `computeRuns` derives from the grid and
 * the unit a segment on the wire round-trips to/from. */
export interface ScheduleRun {
  day: number;
  /** Inclusive start half-hour index. */
  start: number;
  /** Exclusive end half-hour index. */
  end: number;
  brush: PaintedValue;
}

export function createEmptyCells(): CellValue[][] {
  return Array.from({ length: DAY_COUNT }, () => Array<CellValue>(HALF_HOURS_PER_DAY).fill(null));
}

/** The value a brush paints onto a cell — `clear` paints `null` (a gap), same as every other
 * brush paints its own value; there is no separate "erase" code path. */
export function paintedValueOf(brush: Brush): CellValue {
  if (brush.kind === "persona") return brush.personaId;
  if (brush.kind === "music") return "music";
  return null;
}

export function brushesEqual(a: Brush | null, b: Brush | null): boolean {
  if (a === null || b === null) return a === b;
  if (a.kind !== b.kind) return false;
  return a.kind === "persona" && b.kind === "persona" ? a.personaId === b.personaId : true;
}

/** Immutable single-cell write — returns a new grid, the original is untouched. Cheap at 7×48. */
export function withCellPainted(
  cells: readonly (readonly CellValue[])[],
  day: number,
  halfHour: number,
  value: CellValue
): CellValue[][] {
  return cells.map((row, d) => (d === day ? row.map((v, h) => (h === halfHour ? value : v)) : [...row]));
}

/** Derives every contiguous same-brush run from the grid, day by day, left to right. Gaps
 * (`null`) never produce a run — only painted stretches do. */
export function computeRuns(cells: readonly (readonly CellValue[])[]): ScheduleRun[] {
  const runs: ScheduleRun[] = [];
  for (let day = 0; day < DAY_COUNT; day++) {
    const row = cells[day] ?? [];
    let h = 0;
    while (h < HALF_HOURS_PER_DAY) {
      const value = row[h] ?? null;
      if (value === null) {
        h += 1;
        continue;
      }
      let end = h + 1;
      while (end < HALF_HOURS_PER_DAY && (row[end] ?? null) === value) {
        end += 1;
      }
      runs.push({ day, start: h, end, brush: value });
      h = end;
    }
  }
  return runs;
}

function brushKeyPart(brush: PaintedValue): string {
  return brush === "music" ? "music" : `persona:${brush}`;
}

/**
 * The override map's key for a run: `day:start:brush`. `brush` is part of the key — not just
 * `day:start` — so that erasing a run and later painting a DIFFERENT brush at the same slot never
 * inherits the old block's override (see this module's own doc comment). Two runs with the same
 * `day`/`start`/`brush` are, for override purposes, the same block.
 */
export function runKey(day: number, start: number, brush: PaintedValue): string {
  return `${day}:${start}:${brushKeyPart(brush)}`;
}

/** The exact inverse of {@link runKey} — {@link pruneOverrides}' merge check needs an old entry's
 * `day`/`start`/`brush` back out of the map's own key rather than a second, redundant copy stored
 * alongside it. `brushKeyPart` emits either `music` (3 colon-separated parts total) or
 * `persona:<id>` (4 parts) — both shapes round-trip through this split. */
function parseRunKey(key: string): { day: number; start: number; brush: PaintedValue } {
  const [dayPart, startPart, kindPart, personaPart] = key.split(":");
  const brush: PaintedValue = kindPart === "music" ? "music" : Number(personaPart);
  return { day: Number(dayPart), start: Number(startPart), brush };
}

/**
 * Reconciles the overrides map against the FRESH run set a cell mutation produced — the one rule
 * that implements every case this module's own doc comment enumerates. Call after any cell
 * mutation, passing the overrides map as it stood immediately BEFORE that mutation.
 *
 * An override survives only if its key still names a live run (cases 2 and 4: a moved or erased
 * run's key names nothing live, so it drops out simply by never being visited below). Among runs
 * whose key DOES still match, a forward-extend into a genuinely empty gap (case 1) and a merge
 * that swallowed a separately-overridden run (case 5) look identical by key alone — both keep the
 * same `day:start` — so telling them apart needs the stored `end`: if the run's span grew past its
 * old `end` AND that newly-covered range contains some OTHER old override's own `start` (same day,
 * same brush), this mutation absorbed a distinct overridden run — case 5 drops both. A plain
 * shrink (case 3's surviving leftmost split piece) never triggers this check at all, since it only
 * looks at growth past the old `end`.
 */
export function pruneOverrides(
  overrides: ReadonlyMap<string, StoredOverride>,
  runs: readonly ScheduleRun[]
): OverridesMap {
  function absorbedAnotherOverride(run: ScheduleRun, ownKey: string, oldEnd: number): boolean {
    if (run.end <= oldEnd) return false; // shrank or unchanged — nothing new to have absorbed.
    for (const otherKey of overrides.keys()) {
      if (otherKey === ownKey) continue;
      const other = parseRunKey(otherKey);
      if (other.day === run.day && other.brush === run.brush && other.start >= oldEnd && other.start < run.end) {
        return true;
      }
    }
    return false;
  }

  const next: OverridesMap = new Map();
  for (const run of runs) {
    const key = runKey(run.day, run.start, run.brush);
    const entry = overrides.get(key);
    if (entry === undefined) continue; // no override was ever set for this exact run identity.
    if (absorbedAnotherOverride(run, key, entry.end)) continue; // merge with another overridden run — drop both.
    next.set(key, { end: run.end, overrides: entry.overrides });
  }
  return next;
}

export function findRunAt(runs: readonly ScheduleRun[], day: number, halfHour: number): ScheduleRun | null {
  return runs.find((run) => run.day === day && halfHour >= run.start && halfHour < run.end) ?? null;
}

export function findRunByStart(runs: readonly ScheduleRun[], day: number, start: number): ScheduleRun | null {
  return runs.find((run) => run.day === day && run.start === start) ?? null;
}

/**
 * Builds the local grid + overrides map from a `GET /api/schedule` (or a PUT's 200 response) week
 * document. Known, documented limitation: if the server ever returns two ADJACENT same-persona
 * segments with different envelope overrides, loading them onto the grid merges them into one run
 * (the grid has no per-cell memory of segment boundaries, only contiguous same-brush runs) — this
 * function itself sets an entry for EACH segment's own `day:start:brush` key, but `ScheduleEditor`'s
 * mount-time reconciliation (the same `pruneOverrides` cells-effect every later mutation runs
 * through) can't tell "two segments that happened to load adjacent" apart from "an operator merge"
 * (case 5 on {@link pruneOverrides}'s own doc comment) — so BOTH overrides drop, not just the
 * trailing one. This is an inherent consequence of the paint model (SPEC F94.3 designed the grid as
 * the source of truth, not a segment list) rather than a bug in this function; every week this
 * editor itself SAVES is already collapsed to non-adjacent-mergeable runs by construction, so this
 * only bites on a week authored some other way.
 */
export function deriveGridFromWeek(week: ScheduleWeekDto): {
  cells: CellValue[][];
  overrides: OverridesMap;
} {
  const cells = createEmptyCells();
  const overrides: OverridesMap = new Map();

  for (const segment of week.segments) {
    if (segment.day < 0 || segment.day >= DAY_COUNT) continue;
    const start = Math.max(0, Math.floor(segment.startMinute / MINUTES_PER_HALF_HOUR));
    const end = Math.min(HALF_HOURS_PER_DAY, Math.ceil(segment.endMinute / MINUTES_PER_HALF_HOUR));
    if (end <= start) continue;

    const value: PaintedValue = segment.personaId === null ? "music" : segment.personaId;
    const row = cells[segment.day];
    if (row === undefined) continue; // unreachable — segment.day is already range-checked above.
    for (let h = start; h < end; h++) {
      row[h] = value;
    }

    const hasOverrides =
      segment.genres !== null || segment.energyMin !== null || segment.energyMax !== null || segment.showId !== null;
    if (hasOverrides) {
      overrides.set(runKey(segment.day, start, value), {
        end,
        overrides: {
          genres: segment.genres,
          energyMin: segment.energyMin,
          energyMax: segment.energyMax,
          showId: segment.showId,
        },
      });
    }
  }

  return { cells, overrides };
}

/** Serializes the grid + overrides to the whole-week wire document a `PUT /api/schedule` sends.
 * `id` is always `null` — the server ignores it on write (see `ScheduleSegmentDto`'s own remarks in
 * `types.ts`). A run with no stored override serializes its three envelope fields as `null`
 * (station default), never inventing a value the operator never set. */
export function serializeWeek(
  cells: readonly (readonly CellValue[])[],
  overrides: ReadonlyMap<string, StoredOverride>
): ScheduleWeekDto {
  const segments: ScheduleSegmentDto[] = computeRuns(cells).map((run) => {
    const override = overrides.get(runKey(run.day, run.start, run.brush))?.overrides ?? NO_OVERRIDES;
    return {
      id: null,
      day: run.day,
      startMinute: run.start * MINUTES_PER_HALF_HOUR,
      endMinute: run.end * MINUTES_PER_HALF_HOUR,
      personaId: run.brush === "music" ? null : run.brush,
      genres: override.genres,
      energyMin: override.energyMin,
      energyMax: override.energyMax,
      showId: override.showId,
    };
  });
  return { segments };
}

/** A small deterministic palette (SPEC F94.3's "judge the palette" call) — see `globals.css`'s
 * `--sched-N` tokens for the actual Wireless-compatible hues. Cycles for a roster bigger than the
 * palette; two personas CAN share a hue on a large roster (the block's own DJ-name label, not the
 * color alone, is what disambiguates — same posture the design-aesthetic skill takes toward
 * accent-color scarcity elsewhere in this app). */
// Written out as literal strings, NOT built via `` `bg-sched-${n}` `` — Tailwind's content scanner
// finds utility classes by scanning source text for literal tokens; a template-literal-interpolated
// class name never appears as a whole token anywhere in the built source; it silently generates NO
// CSS for any of the 6 (a real bug caught in this task's own browser-facing self-test: the compiled
// stylesheet had zero `.bg-sched-N` rules until this list existed as plain strings).
const PERSONA_SWATCH_CLASSES: readonly string[] = [
  "bg-sched-1",
  "bg-sched-2",
  "bg-sched-3",
  "bg-sched-4",
  "bg-sched-5",
  "bg-sched-6",
];

/** The Tailwind class name for a persona's swatch fill — the only supported way to get one; see
 * this constant's own remarks for why callers must never re-derive `bg-sched-N` themselves. */
export function personaSwatchClassName(personaId: number): string {
  const index = Math.abs(Math.trunc(personaId)) % PERSONA_SWATCH_CLASSES.length;
  return PERSONA_SWATCH_CLASSES[index] ?? "bg-sched-1";
}

export function formatHalfHourLabel(halfHour: number): string {
  const totalMinutes = halfHour * MINUTES_PER_HALF_HOUR;
  const hh = Math.floor(totalMinutes / 60);
  const mm = totalMinutes % 60;
  return `${String(hh).padStart(2, "0")}:${String(mm).padStart(2, "0")}`;
}

/** `end` is exclusive in half-hour units and may legitimately be 48 (24:00, midnight) — this
 * formats that boundary literally rather than wrapping it back to "00:00", which would read as the
 * block ending at the START of the day it actually runs through to. */
export function formatRunTimeRange(start: number, end: number): string {
  const endLabel = end === HALF_HOURS_PER_DAY ? "24:00" : formatHalfHourLabel(end);
  return `${formatHalfHourLabel(start)}–${endLabel}`;
}

/** True when a `PUT /api/schedule` 400's cell error names exactly this run — used to highlight the
 * offending block in place (STORY-248 AC, "per-cell 400 errors highlighted in place"). Matches on
 * the three wire fields the server echoes back, not `rowIndex`: those are the same values this
 * editor submitted for the run, so a match is exact without needing to remember submission order
 * across the request/response boundary. */
export function cellErrorMatchesRun(error: ScheduleCellErrorDto, run: ScheduleRun): boolean {
  return (
    error.day === run.day &&
    error.startMinute === run.start * MINUTES_PER_HALF_HOUR &&
    error.endMinute === run.end * MINUTES_PER_HALF_HOUR
  );
}

/** Builds the id→name lookup the grid/panel use to label a persona-painted block. */
export function personaNameMap(personas: readonly RosterPersonaDto[]): Map<number, string> {
  return new Map(personas.map((persona) => [persona.id, persona.name] as const));
}
