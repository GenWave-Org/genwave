"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import type { ScheduleShowsStatus } from "./types";

/**
 * What Assign would post, or why it can't (STORY-313 P7, reviewer finding): collapses the OLD
 * `blockId: number | null` + `disabledReason: string | null` pair into one union so the impossible
 * combination — both `null` at once — has no value that inhabits it. `ScheduleEditor` already folds
 * a `null` block id into its own disabled-reason branch (`openBlockId === null` → "Reload the
 * schedule…") before this component ever sees it; this type just makes that fold the ONLY shape the
 * caller can hand over, instead of two independently-nullable fields the caller COULD (but happens
 * not to) desync. `narrowDisabledReason` rides inside `"ready"` only — it never matters while the
 * picker is disabled outright. */
export type ScheduleShowPickerTarget =
  | {
      kind: "ready";
      /** The stored `segment_schedule` row id for the open run's own leftmost half-hour (see
       * `schedule-grid-model`'s `findBlockId`) — the `blockId` a `POST /api/schedule/assign-show`
       * call addresses. */
      blockId: number;
      /** Non-`null` disables ONLY the "Apply to the whole run" checkbox (run-wide assign stays
       * available and correct) — STORY-313 P2: a visual run can merge more than one STORED
       * segment (`schedule-grid-model`'s `countStoredSegmentsInRun`), and narrowing to "just this
       * block" would only ever be able to name the leftmost of them, silently abandoning the rest
       * of the run the operator is actually looking at. */
      narrowDisabledReason: string | null;
    }
  | {
      kind: "disabled";
      /** T245 wire-contract decision (a): the picker only ever acts on the SAVED grid, since the
       * endpoint's `blockId` addresses a STORED row and its run-span is computed against stored
       * rows — an in-flight, unsaved paint stroke has no server-side identity yet to assign
       * against. */
      reason: string;
    };

export interface ScheduleShowPickerProps {
  /** What Assign would post, or why every control is disabled — see {@link ScheduleShowPickerTarget}. */
  target: ScheduleShowPickerTarget;
  /** The block's currently-assigned show id (this run's own `overrides.showId`), or `null` for
   * unnamed — always the SAVED value; there is no local "draft" assignment to hold here (SPEC
   * F119.2: the endpoint writes immediately, there is no Save step for it). Independent of `target`:
   * the "Current: …" line renders this regardless of whether the controls below are armed. */
  currentShowId: number | null;
  /** The show roster this picker's `<select>` lists — loaded ONCE, server-side, by the schedule page
   * (STORY-313 P6) and threaded down through `ScheduleEditor`/`ScheduleEnvelopePanel` verbatim; this
   * component never fetches its own copy. `"error"` degrades every control below to disabled — see
   * this prop's own type doc in `types.ts` for why the armed-while-unloaded window this replaces
   * can't exist anymore (STORY-313 P1). */
  shows: ScheduleShowsStatus;
  /** Fired after a successful assignment. The response only names updated block ids plus a fresh
   * version (`AssignShowResponseDto`), never the whole week document — the caller (`ScheduleEditor`)
   * re-syncs `cells`/`overrides`/`segments`/`weekVersion` from a follow-up `GET /api/schedule` (safe:
   * the round trip preserves `showId` — T243's own reviewer note). */
  onAssigned: () => void;
}

interface AssignShowResponseBody {
  updatedBlockIds: number[];
  version: string;
}

const FIELD_LABEL_CLASSES = "text-[0.78rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * The grid side panel's show picker (SPEC F119.2, STORY-313, PLAN T245): by default, Assign names
 * the contiguous same-brush run containing the open block — music-only runs span too, Dean's
 * 2026-08-10 ratified ruling (`ScheduleRepository.ComputeRun`'s own remarks carry the full rule);
 * unchecking "Apply to the whole run" narrows to the single clicked block instead (disabled outright
 * when that single block isn't well-defined — see {@link ScheduleShowPickerTarget}'s own remarks on
 * `narrowDisabledReason`).
 *
 * ── Why this owns the assign round trip, unlike every other field in this panel ─────────────────
 * Every OTHER field `ScheduleEnvelopePanel` renders (genres/energy) writes into `ScheduleEditor`'s
 * in-memory `overrides` map and waits for the operator's own explicit Save — SPEC F94.3's "one PUT,
 * no autosave" rule. `POST /api/schedule/assign-show` is deliberately a DIFFERENT surface (SPEC
 * F119.2): it writes the store immediately, is transactional, and computes the run server-side — the
 * ONE source of truth for "where does this run end" (this component never re-derives that itself;
 * see {@link ScheduleShowPickerTarget}'s own remarks — it only ever sends the one clicked block's own
 * id, never a client-computed list). This component therefore owns the assign POST and only ever
 * reports SUCCESS upward via `onAssigned` — `ScheduleEditor` still owns re-deriving the grid's own
 * local state afterward, the same way it already owns that after a Save's own 200.
 *
 * ── The show LIST is no longer this component's own concern (STORY-313 P1/P6) ───────────────────
 * `GET /api/shows` used to be fetched here, once on mount — but that left a real window where a
 * click could ARM Assign (the picker still showed the block's stored `showId`, the button wasn't
 * disabled) before the list resolved, or forever if it never did: a click there posted the block's
 * EXISTING showId while the select still read "No show", silently re-affirming an assignment the
 * operator never chose. The roster now loads ONCE, server-side, in the schedule page's own
 * `Promise.allSettled` (mirroring the persona palette) and arrives here as the `shows` prop —
 * already resolved to `"loaded"` or `"error"` by the time this component ever mounts, so there is no
 * loading tick left to race. `"error"` disables the select/checkbox/Assign the same way `target`'s
 * own `"disabled"` variant does; a failed roster load degrades this section alone (SPEC F119.3's
 * coverage-neutral posture), the rest of the panel (genres/energy/Delete) stays untouched.
 */
export function ScheduleShowPicker({
  target,
  currentShowId,
  shows,
  onAssigned,
}: ScheduleShowPickerProps): ReactNode {
  // Seeded once from the block's current assignment, then operator-driven — the same "seed on
  // mount, never re-synced" idiom `ScheduleEnvelopePanel`'s own genre/energy fields use. A DIFFERENT
  // block remounts this whole component (ScheduleEditor's `key={runKey(...)}` on the panel), so
  // there is no stale-seed risk across blocks; a SUCCESSFUL assign on THIS block needs no re-seed
  // either — the select already shows exactly what was just submitted.
  const [selectedShowId, setSelectedShowId] = useState(currentShowId === null ? "" : String(currentShowId));
  const [applyToRun, setApplyToRun] = useState(true);
  const [isAssigning, setIsAssigning] = useState(false);

  const narrowDisabledReason = target.kind === "ready" ? target.narrowDisabledReason : null;
  // Armed only once BOTH the target is a real, saved block AND the roster actually loaded — the P1
  // fix: there is no value either can hold that leaves this `true` while the other is missing.
  const isArmed = target.kind === "ready" && shows.kind === "loaded";
  const isNarrowDisabled = !isArmed || narrowDisabledReason !== null;

  const loadedShows = shows.kind === "loaded" ? shows.shows : [];
  const selectedShow = loadedShows.find((show) => String(show.id) === selectedShowId) ?? null;
  const currentShowName =
    currentShowId === null
      ? "None"
      : shows.kind === "error"
        ? "Unavailable"
        : (loadedShows.find((show) => show.id === currentShowId)?.name ?? "Unknown show");

  async function handleAssign(): Promise<void> {
    if (target.kind !== "ready") return; // Unreachable via the UI — the button is disabled below regardless.
    const blockId = target.blockId;
    const clearing = selectedShowId === "";
    setIsAssigning(true);
    try {
      const resp = await fetch("/api/schedule/assign-show", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          blockId,
          showId: clearing ? null : Number(selectedShowId),
          // Narrowing is unavailable whenever `narrowDisabledReason` is set — the checkbox is
          // disabled in that case (so `applyToRun` can never actually read `false` from the UI),
          // but forcing it here too means this request is correct even if that ever drifts.
          applyToRun: narrowDisabledReason !== null ? true : applyToRun,
        }),
      });

      if (resp.status === 200) {
        const body = (await resp.json()) as AssignShowResponseBody;
        const count = body.updatedBlockIds.length;
        const verb = clearing ? "cleared from" : "assigned to";
        toast.success(`Show ${verb} ${count} block${count === 1 ? "" : "s"}.`);
        onAssigned();
        return;
      }

      toast.error(await readErrorMessage(resp));
    } catch {
      toast.error("Network error — check your connection");
    } finally {
      setIsAssigning(false);
    }
  }

  return (
    <div className="flex flex-col gap-3 border-t border-line pt-3">
      <p className={FIELD_LABEL_CLASSES}>Show</p>
      <p className="text-[0.85rem] text-ink">Current: {currentShowName}</p>

      {target.kind === "disabled" && <p className="text-[0.78rem] text-mute">{target.reason}</p>}
      {shows.kind === "error" && (
        <p role="alert" className="text-[0.78rem] text-danger">
          Show list unavailable — reload the page to assign a show.
        </p>
      )}

      <div className="flex flex-col gap-1.5">
        <label htmlFor="schedule-block-show" className={FIELD_LABEL_CLASSES}>
          Assign show
        </label>
        <select
          id="schedule-block-show"
          value={selectedShowId}
          disabled={!isArmed}
          onChange={(e) => setSelectedShowId(e.currentTarget.value)}
          className={FIELD_INPUT_CLASSES}
        >
          <option value="">No show</option>
          {loadedShows.map((show) => (
            <option key={show.id} value={show.id}>
              {show.name}
            </option>
          ))}
        </select>
        {selectedShow?.tagline !== null && selectedShow?.tagline !== undefined && selectedShow.tagline !== "" && (
          <p className="text-[0.78rem] text-mute">{selectedShow.tagline}</p>
        )}
      </div>

      <label className="flex items-center gap-2 text-[0.85rem] text-ink">
        <input
          type="checkbox"
          checked={applyToRun}
          disabled={isNarrowDisabled}
          onChange={(e) => setApplyToRun(e.currentTarget.checked)}
        />
        Apply to the whole run
      </label>
      {isArmed && narrowDisabledReason !== null && <p className="text-[0.78rem] text-mute">{narrowDisabledReason}</p>}

      <Button
        type="button"
        disabled={!isArmed || isAssigning}
        onClick={() => {
          void handleAssign();
        }}
      >
        {isAssigning ? "Assigning…" : "Assign"}
      </Button>
    </div>
  );
}
