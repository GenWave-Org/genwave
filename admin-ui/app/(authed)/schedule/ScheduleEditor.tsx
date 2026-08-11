"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { ScheduleEnvelopePanel } from "./ScheduleEnvelopePanel";
import { ScheduleGrid, type NowMarker } from "./ScheduleGrid";
import { SchedulePalette } from "./SchedulePalette";
import type { ScheduleShowPickerTarget } from "./ScheduleShowPicker";
import {
  computeRuns,
  countStoredSegmentsInRun,
  deriveGridFromWeek,
  findBlockId,
  findRunAt,
  findRunByStart,
  HALF_HOURS_PER_DAY,
  paintedValueOf,
  personaNameMap,
  pruneOverrides,
  runKey,
  serializeWeek,
  withCellPainted,
  type BlockOverrides,
  type Brush,
  type CellValue,
} from "./schedule-grid-model";
import type {
  RosterPersonaDto,
  ScheduleCellErrorDto,
  ScheduleSegmentDto,
  ScheduleShowsStatus,
  ScheduleWeekDto,
} from "./types";

export interface ScheduleEditorProps {
  initialWeek: ScheduleWeekDto;
  personas: readonly RosterPersonaDto[];
  /** The show roster for the side panel's picker (STORY-313 P6) — loaded ONCE, server-side, by the
   * schedule page's own `Promise.allSettled` (mirroring `personas`) and passed straight through to
   * `ScheduleEnvelopePanel`/`ScheduleShowPicker`; this component fetches nothing itself. */
  shows: ScheduleShowsStatus;
}

/** The follow-up `GET /api/schedule` after a successful assignment can itself fail two ways (a
 * non-ok response, or the fetch throwing outright) — both leave the operator in exactly the same
 * spot: the assignment already landed server-side, only the LOCAL re-sync didn't. One shared,
 * honest message for both (STORY-313 P3, reviewer finding: the two branches used to read
 * differently, one generic-error, one recovery-worded) — reloading the page is the actual recovery
 * (a fresh SSR load re-derives `cells`/`overrides`/`segments`/`weekVersion` from scratch), so there
 * is nothing else useful to attempt from inside this handler beyond saying so clearly. */
const FOLLOWUP_REFRESH_FAILED_MESSAGE = "The assignment saved, but the grid couldn't refresh — reload the page.";

interface SavedProblemBody {
  detail?: string;
  cellErrors?: ScheduleCellErrorDto[];
  /** `"staleWeek"` on the gh-#255 optimistic-concurrency 409 — distinguishes it from the
   * persona-race 409, which IS worth a plain retry. */
  conflict?: string;
}

/** Today's day/half-hour, computed once at mount — a static "now" marker on the grid (SPEC F94.3:
 * "cheap and orienting", explicitly not worth a live 1s tick for an editor screen). Browser-local
 * time, the same default every clock formatter in this app already falls back to
 * (`lib/format-clock.ts`) absent a station-timezone concept anywhere in the admin UI. */
function computeNowMarker(): NowMarker {
  const now = new Date();
  const halfHour = Math.min(HALF_HOURS_PER_DAY - 1, Math.floor((now.getHours() * 60 + now.getMinutes()) / 30));
  return { day: now.getDay(), halfHour };
}

/**
 * The drag-paint editor's orchestrator (STORY-248, SPEC F94.3 — PLAN T129, "the or bust
 * deliverable"). Owns every piece of state the paint model needs; `ScheduleGrid`/
 * `SchedulePalette`/`ScheduleEnvelopePanel` are presentation over it.
 *
 * ── Paint vs. inspect ─────────────────────────────────────────────────────────────────────────
 * Exactly one thing decides whether the grid paints or opens the panel: whether a brush is
 * currently selected (`selectedBrush !== null`). With a brush selected, every pointer stroke (or a
 * single click, or a keyboard Enter/Space — `ScheduleGrid`'s own remarks) paints. With NO brush
 * selected — the default state on load, or after clicking the active brush again to deselect it —
 * a click/Enter on an occupied cell opens {@link ScheduleEnvelopePanel} instead, and paints
 * nothing. This is why clicking a freshly-loaded block opens its panel without any extra "select"
 * step: nothing is selected yet.
 *
 * ── Save is the only write ────────────────────────────────────────────────────────────────────
 * Every local mutation (paint, clear, delete-block, an override edit) only touches `cells`/
 * `overrides` in memory — nothing reaches the network until the operator clicks Save, which PUTs
 * the ENTIRE serialized week in one request (SPEC F94.3: no autosave, a schedule write flips the
 * live station). A 200 re-derives local state from the response body (ids refresh); a 400 leaves
 * `cells`/`overrides` completely untouched and instead populates `cellErrors` so the grid
 * highlights the offending blocks in place — the operator's unsaved edit is never dropped
 * (STORY-248 AC5). Save is disabled whenever there is nothing TO save (`!isDirty`), same as while
 * a save is already in flight (`isSaving`) — and a dirty, un-saved edit gets one more guard: a
 * `beforeunload` listener (mounted only while `isDirty`, torn down the instant it isn't) asks the
 * browser to confirm leaving, so a drag-painted week can't vanish on an accidental tab close.
 *
 * PLAN T245 adds exactly one exception: the side panel's show picker (`ScheduleShowPicker`) posts to
 * `POST /api/schedule/assign-show` immediately on its own Assign action, no Save step — SPEC F119.2's
 * dedicated, transactional, server-computed-run endpoint. It is disabled whenever `isDirty` is true
 * (the endpoint addresses a STORED row by id; an unsaved paint stroke has none yet), so it can never
 * race a pending Save. `handleShowAssigned` is the one place this component reaches back to the
 * server on its own initiative afterward — a follow-up `GET /api/schedule` (safe, since the round
 * trip preserves `showId` — T243's own reviewer note) that re-syncs `cells`/`overrides`/`segments`/
 * `weekVersion` the same way a Save's own 200 does.
 *
 * ── One cell mutation can paint several cells in a single React batch ───────────────────────────
 * A drag paints every cell the pointer crosses (`ScheduleGrid`'s pointermove hit-test) — several
 * `onPaintCell` calls can land inside ONE React batch (e.g. a fast drag, or a spec driving multiple
 * synthetic pointermoves back-to-back with no `await` between them). `commitCells` therefore takes
 * an UPDATER (`(prev) => next`), never a precomputed array: `setCells(update)` lets React apply
 * each queued update against the PREVIOUS update's result, so a batch of N paints keeps all N —
 * passing an already-computed `CellValue[][]` closed over the stale pre-batch `cells` would let
 * later calls in the same batch silently overwrite earlier ones (only the LAST paint would stick).
 * Reconciling `overrides` against the mutation's OWN result can't happen inside that same updater
 * (a second `setState` call from within another state updater is exactly the kind of impurity React
 * warns about) — so it runs in an effect keyed on `cells` instead, which by construction only ever
 * sees the fully-batched, post-commit value.
 */
export function ScheduleEditor({ initialWeek, personas, shows }: ScheduleEditorProps): ReactNode {
  const initialGrid = deriveGridFromWeek(initialWeek);
  const [cells, setCells] = useState<CellValue[][]>(initialGrid.cells);
  const [overrides, setOverrides] = useState(initialGrid.overrides);
  // The RAW segment list backing `cells`/`overrides` — the one place a stored row's own id survives
  // (see `findBlockId`'s own remarks: `cells` has no id at all). Kept in lockstep with `cells`/
  // `overrides` on every server re-sync (mount, a Save's 200, a show assignment's follow-up GET) —
  // never touched by a local paint edit, the same "network response only" discipline `weekVersion`
  // already follows.
  const [segments, setSegments] = useState<readonly ScheduleSegmentDto[]>(initialWeek.segments);
  const [selectedBrush, setSelectedBrush] = useState<Brush | null>(null);
  const [openBlockAnchor, setOpenBlockAnchor] = useState<{ day: number; start: number } | null>(null);
  const [cellErrors, setCellErrors] = useState<readonly ScheduleCellErrorDto[]>([]);
  const [isDirty, setIsDirty] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  // gh-#255: a failed save must stay visible — a toast alone fades in seconds while the painted
  // grid still LOOKS right, which is exactly how a rejected save read as "saved fine" on the demo
  // box. Set on every non-200 outcome (and the network catch); cleared by the next successful save
  // or the next attempt starting.
  const [saveProblem, setSaveProblem] = useState<string | null>(null);
  // gh-#255: the `version` fingerprint of the week this editor last loaded (mount or PUT 200
  // response) — sent back as `baseVersion` on every save so the server can 409 a full-replace built
  // from stale state instead of silently wiping a week another tab/session saved meanwhile.
  const [weekVersion, setWeekVersion] = useState<string | null>(initialWeek.version ?? null);
  const [nowMarker] = useState<NowMarker>(computeNowMarker);
  const confirm = useConfirm();

  const runs = computeRuns(cells);
  const personaNames = personaNameMap(personas);
  const openRun = openBlockAnchor === null ? null : findRunByStart(runs, openBlockAnchor.day, openBlockAnchor.start);
  const openBlockId = openRun === null ? null : findBlockId(segments, openRun.day, openRun.start);
  const openOverrides =
    openRun === null ? null : (overrides.get(runKey(openRun.day, openRun.start, openRun.brush))?.overrides ?? null);
  // STORY-313 P2: how many STORED segments the open run actually merges — `findBlockId` only ever
  // names the LEFTMOST one, so narrowing to "just this block" is only well-defined when the run is
  // exactly one stored row (see `countStoredSegmentsInRun`'s own remarks).
  const openRunSegmentCount =
    openRun === null ? 0 : countStoredSegmentsInRun(segments, openRun.day, openRun.start, openRun.end);
  const narrowDisabledReason =
    openRunSegmentCount > 1
      ? `This run merges ${openRunSegmentCount} saved blocks — only "Apply to the whole run" is available.`
      : null;
  // T245 wire-contract decision (a): the show picker only ever acts on the SAVED grid — `isDirty`
  // takes priority (the common case an operator hits), `openBlockId === null` is the residual
  // edge case (see `findBlockId`'s own remarks) once nothing is dirty. STORY-313 P7: `blockId` and
  // its disabled reason are folded into one union here — the caller-side fold the reviewer asked
  // for — so `ScheduleShowPicker` never has to reconcile two independently-nullable fields itself.
  const showPickerTarget: ScheduleShowPickerTarget = isDirty
    ? { kind: "disabled", reason: "Save your changes before assigning a show." }
    : openBlockId === null
      ? { kind: "disabled", reason: "Reload the schedule to assign a show for this block." }
      : { kind: "ready", blockId: openBlockId, narrowDisabledReason };

  // Reconciles `overrides` against whatever `cells` actually ended up as, every time it changes —
  // see this component's own doc comment for why this can't live inside `commitCells` itself. A
  // no-op on the initial render (the mounted `overrides` already matches the mounted `cells`, both
  // derived from the same `initialWeek` a moment ago) beyond one harmless, equal-by-value re-set.
  useEffect(() => {
    setOverrides((prev) => pruneOverrides(prev, computeRuns(cells)));
  }, [cells]);

  // A dirty, unsaved paint stroke asks before the tab closes — mounted only while there IS
  // something to lose, torn down the instant a save clears `isDirty` (or the editor unmounts).
  useEffect(() => {
    if (!isDirty) return;
    function handleBeforeUnload(e: BeforeUnloadEvent): void {
      e.preventDefault();
    }
    window.addEventListener("beforeunload", handleBeforeUnload);
    return () => {
      window.removeEventListener("beforeunload", handleBeforeUnload);
    };
  }, [isDirty]);

  /** The one path every cell-mutating action funnels through — see this component's own doc
   * comment for why `update` is an updater, not a precomputed grid. Also clears `cellErrors`: those
   * describe the LAST save attempt, and any further edit makes them stale (a highlighted block the
   * operator has since repainted, or moved past, shouldn't keep showing a prior rejection). */
  function commitCells(update: (prev: CellValue[][]) => CellValue[][]): void {
    setCells(update);
    setIsDirty(true);
    setCellErrors([]);
  }

  function handlePaintCell(day: number, halfHour: number): void {
    if (selectedBrush === null) return; // ScheduleGrid only calls this when a brush IS selected.
    const value = paintedValueOf(selectedBrush);
    commitCells((prev) => withCellPainted(prev, day, halfHour, value));
  }

  function handleInspectCell(day: number, halfHour: number): void {
    const run = findRunAt(runs, day, halfHour);
    if (run === null) return; // an empty cell has nothing to inspect.
    setOpenBlockAnchor({ day: run.day, start: run.start });
  }

  /** `end` is the open run's current end — threaded in from the caller's own `openRun` rather than
   * re-derived here, since the caller already has the exact run this edit targets. Needed to build
   * a {@link StoredOverride} entry (`schedule-grid-model`'s merge-detection anchor). */
  function handleChangeOverrides(
    day: number,
    start: number,
    end: number,
    brush: CellValue,
    patch: Partial<BlockOverrides>
  ): void {
    if (brush === null) return;
    const key = runKey(day, start, brush);
    setOverrides((prev) => {
      const next = new Map(prev);
      const existing = next.get(key)?.overrides ?? { genres: null, energyMin: null, energyMax: null, showId: null };
      next.set(key, { end, overrides: { ...existing, ...patch } });
      return next;
    });
    setIsDirty(true);
  }

  async function handleDeleteBlock(day: number, start: number, end: number): Promise<void> {
    const ok = await confirm({
      title: "Delete block",
      consequence: "This clears the block from your local edit — nothing changes on air until you Save.",
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!ok) return;

    commitCells((prev) =>
      prev.map((row, d) => (d === day ? row.map((value, h) => (h >= start && h < end ? null : value)) : row))
    );
    setOpenBlockAnchor(null);
  }

  async function handleSave(): Promise<void> {
    setIsSaving(true);
    setSaveProblem(null);
    // `baseVersion` + `keepalive` (gh-#255): the version pins which stored week this full-replace
    // may overwrite (a stale editor gets a 409 instead of silently destroying newer saves), and
    // keepalive lets an in-flight save finish even if the operator immediately navigates/reloads to
    // verify it — an aborted PUT was one more way a "saved" week never actually reached the server.
    const body: ScheduleWeekDto = { ...serializeWeek(cells, overrides), baseVersion: weekVersion };

    try {
      const resp = await fetch("/api/schedule", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
        keepalive: true,
      });

      if (resp.status === 200) {
        const week = (await resp.json()) as ScheduleWeekDto;
        const derived = deriveGridFromWeek(week);
        setCells(derived.cells);
        setOverrides(derived.overrides);
        setSegments(week.segments);
        setCellErrors([]);
        setOpenBlockAnchor(null);
        setIsDirty(false);
        setWeekVersion(week.version ?? null);
        toast.success("Schedule saved.");
        return;
      }

      if (resp.status === 400) {
        const problem = (await resp.json().catch(() => ({}))) as SavedProblemBody;
        const errors = problem.cellErrors ?? [];
        setCellErrors(errors);
        const message = problem.detail ?? `${errors.length} segment(s) rejected — see the highlighted cells.`;
        setSaveProblem(message);
        toast.error(message);
        // cells/overrides are deliberately left untouched — the rejected edit survives (AC5).
        return;
      }

      if (resp.status === 409) {
        const problem = (await resp.json().catch(() => ({}))) as SavedProblemBody;
        const message =
          problem.conflict === "staleWeek"
            ? problem.detail ??
              "The schedule changed since this page loaded (another tab or session saved). Reload to see the latest — your unsaved painting stays here until you do."
            : problem.detail ?? "The schedule conflicted with a concurrent change. Reload and try again.";
        setSaveProblem(message);
        toast.error(message);
        // Same AC5 posture as the 400 branch: the operator's paint survives on screen.
        return;
      }

      const message = await readErrorMessage(resp);
      setSaveProblem(message);
      toast.error(message);
    } catch {
      setSaveProblem("Network error — the schedule was NOT saved. Check your connection and save again.");
      toast.error("Network error — check your connection");
    } finally {
      setIsSaving(false);
    }
  }

  /**
   * `ScheduleShowPicker`'s own `onAssigned` callback (T245): a `POST /api/schedule/assign-show` just
   * succeeded, but its response only names updated block ids plus a fresh version — never the whole
   * week document — so re-syncing `cells`/`overrides`/`segments`/`weekVersion` needs a follow-up
   * `GET /api/schedule` (safe: the round trip preserves `showId`, T243's own reviewer note). Mirrors
   * `handleSave`'s own 200 branch exactly, minus the save-specific bookkeeping (`isDirty`/
   * `cellErrors`/`openBlockAnchor` are all untouched by an assignment — see this component's own doc
   * comment). The open panel stays mounted throughout: an assignment never changes a run's own
   * day/start/brush, so `runKey` — and therefore the panel's own React `key` — never changes either.
   *
   * Both failure shapes (a non-ok response, or the fetch itself throwing) leave local state exactly
   * as it stood before this call — deliberately NOT patched here (STORY-313 P3, reviewer finding):
   * the shared {@link FOLLOWUP_REFRESH_FAILED_MESSAGE} already tells the operator the real recovery
   * (reload the page), which re-derives every one of these fields fresh from the server; there is no
   * partial, in-place fix-up worth attempting from inside this handler.
   */
  async function handleShowAssigned(): Promise<void> {
    try {
      const resp = await fetch("/api/schedule");
      if (!resp.ok) {
        toast.error(FOLLOWUP_REFRESH_FAILED_MESSAGE);
        return;
      }
      const week = (await resp.json()) as ScheduleWeekDto;
      const derived = deriveGridFromWeek(week);
      setCells(derived.cells);
      setOverrides(derived.overrides);
      setSegments(week.segments);
      setWeekVersion(week.version ?? null);
    } catch {
      toast.error(FOLLOWUP_REFRESH_FAILED_MESSAGE);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <SchedulePalette personas={personas} selectedBrush={selectedBrush} onSelectBrush={setSelectedBrush} />
        <div className="flex items-center gap-3">
          {isDirty && (
            <span role="status" className="text-[0.78rem] text-mute">
              Unsaved changes
            </span>
          )}
          <Button
            type="button"
            onClick={() => {
              void handleSave();
            }}
            disabled={isSaving || !isDirty}
          >
            {isSaving ? "Saving…" : "Save schedule"}
          </Button>
        </div>
      </div>

      {saveProblem !== null && (
        <div
          role="alert"
          className="rounded-[6px] border border-danger bg-danger/10 px-3 py-2 text-[0.85rem] text-danger"
        >
          {saveProblem}
        </div>
      )}

      <div className="flex flex-col items-start gap-4 sm:flex-row">
        <div className="min-w-0 flex-1">
          <ScheduleGrid
            cells={cells}
            runs={runs}
            personaNames={personaNames}
            hasBrushSelected={selectedBrush !== null}
            cellErrors={cellErrors}
            nowMarker={nowMarker}
            onPaintCell={handlePaintCell}
            onInspectCell={handleInspectCell}
          />
        </div>

        {openRun !== null && (
          <ScheduleEnvelopePanel
            key={runKey(openRun.day, openRun.start, openRun.brush)}
            run={openRun}
            personaName={openRun.brush === "music" ? null : personaNames.get(openRun.brush) ?? null}
            overrides={openOverrides}
            onChangeOverrides={(patch) =>
              handleChangeOverrides(openRun.day, openRun.start, openRun.end, openRun.brush, patch)
            }
            onDelete={() => {
              void handleDeleteBlock(openRun.day, openRun.start, openRun.end);
            }}
            onClose={() => setOpenBlockAnchor(null)}
            showPicker={{
              target: showPickerTarget,
              currentShowId: openOverrides?.showId ?? null,
              shows,
              onAssigned: () => {
                void handleShowAssigned();
              },
            }}
          />
        )}
      </div>
    </div>
  );
}
