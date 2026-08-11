"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { formatRunTimeRange, DAY_FULL_NAMES, type BlockOverrides, type ScheduleRun } from "./schedule-grid-model";
import { ScheduleShowPicker, type ScheduleShowPickerProps } from "./ScheduleShowPicker";

export interface ScheduleEnvelopePanelProps {
  run: ScheduleRun;
  /** `null` for the music-only brush — the panel still opens (blocks are inspectable regardless of
   * brush), it just has no DJ name to show. */
  personaName: string | null;
  /** The block's CURRENT stored override, or `null` when it has none (station default on every
   * field). Sourced fresh each time a DIFFERENT block opens — this component's own local text state
   * is seeded from it once, on mount, and never re-synced (see this component's render key note on
   * `ScheduleEditor`). */
  overrides: BlockOverrides | null;
  onChangeOverrides: (patch: Partial<BlockOverrides>) => void;
  onDelete: () => void;
  onClose: () => void;
  /** Everything the show-picker section (`ScheduleShowPicker`, SPEC F119.2, PLAN T245) needs —
   * passed through verbatim. This panel has no opinion of its own about show assignment beyond
   * rendering the section between the envelope fields and Delete. */
  showPicker: ScheduleShowPickerProps;
}

const FIELD_LABEL_CLASSES = "text-[0.78rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES = "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink";

/** Parses a comma-separated genre list into the wire shape: trimmed, non-empty entries; blank text
 * (or a list that trims down to nothing) is the station-default sentinel, `null` — never an empty
 * array pretending to be "no genres entered yet" (SPEC F94.3: blank envelope fields serialize as
 * station default). */
function parseGenres(text: string): string[] | null {
  const entries = text
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry !== "");
  return entries.length === 0 ? null : entries;
}

/** Blank text is station-default (`null`); anything that doesn't parse to a finite number is
 * treated the same way rather than silently coercing to 0 — an operator mid-edit (e.g. typing
 * "0.") sees their own keystrokes in the field regardless, since the input is bound to local TEXT
 * state, not this parsed value. */
function parseEnergy(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed === "") return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * The envelope side panel (STORY-248, SPEC F94.3) — opens for whichever block
 * `ScheduleEditor` currently has selected. Shows the DJ name (or "Music only"), day, and time
 * range, plus optional genre/energy overrides that attach to THIS run (see `schedule-grid-model`'s
 * own doc comment for exactly how an override's identity survives — or doesn't — across a repaint).
 * Every field commits on change, immediately, into `ScheduleEditor`'s in-memory overrides map —
 * there is no separate "apply" step here; the one PUT at Save time is what actually persists it
 * (SPEC F94.3: "one PUT with the whole week", no autosave anywhere in this editor).
 */
export function ScheduleEnvelopePanel({
  run,
  personaName,
  overrides,
  onChangeOverrides,
  onDelete,
  onClose,
  showPicker,
}: ScheduleEnvelopePanelProps): ReactNode {
  const [genresText, setGenresText] = useState(overrides?.genres?.join(", ") ?? "");
  const [energyMinText, setEnergyMinText] = useState(overrides?.energyMin?.toString() ?? "");
  const [energyMaxText, setEnergyMaxText] = useState(overrides?.energyMax?.toString() ?? "");

  const title = personaName ?? "Music only";

  return (
    <aside
      aria-label={`${title} block details`}
      className="flex w-full flex-col gap-4 rounded-[6px] border border-line bg-surface p-4 sm:w-[280px]"
    >
      <div className="flex items-start justify-between gap-2">
        <div>
          <p className="font-display text-[1.05rem] text-ink">{title}</p>
          <p className="text-[0.82rem] text-mute">
            {DAY_FULL_NAMES[run.day]} · {formatRunTimeRange(run.start, run.end)}
          </p>
        </div>
        <Button type="button" variant="secondary" aria-label="Close block details" onClick={onClose}>
          Close
        </Button>
      </div>

      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <label htmlFor="schedule-block-genres" className={FIELD_LABEL_CLASSES}>
            Genres (comma-separated, blank = station default)
          </label>
          <input
            id="schedule-block-genres"
            type="text"
            value={genresText}
            onChange={(e) => {
              const text = e.currentTarget.value;
              setGenresText(text);
              onChangeOverrides({ genres: parseGenres(text) });
            }}
            className={FIELD_INPUT_CLASSES}
          />
        </div>

        <div className="flex gap-3">
          <div className="flex flex-1 flex-col gap-1.5">
            <label htmlFor="schedule-block-energy-min" className={FIELD_LABEL_CLASSES}>
              Energy min
            </label>
            <input
              id="schedule-block-energy-min"
              type="number"
              step="0.01"
              min="0"
              max="1"
              value={energyMinText}
              onChange={(e) => {
                const text = e.currentTarget.value;
                setEnergyMinText(text);
                onChangeOverrides({ energyMin: parseEnergy(text) });
              }}
              className={FIELD_INPUT_CLASSES}
            />
          </div>
          <div className="flex flex-1 flex-col gap-1.5">
            <label htmlFor="schedule-block-energy-max" className={FIELD_LABEL_CLASSES}>
              Energy max
            </label>
            <input
              id="schedule-block-energy-max"
              type="number"
              step="0.01"
              min="0"
              max="1"
              value={energyMaxText}
              onChange={(e) => {
                const text = e.currentTarget.value;
                setEnergyMaxText(text);
                onChangeOverrides({ energyMax: parseEnergy(text) });
              }}
              className={FIELD_INPUT_CLASSES}
            />
          </div>
        </div>
      </div>

      <ScheduleShowPicker {...showPicker} />

      <Button type="button" variant="destructive" aria-label={`Delete ${title} block`} onClick={onDelete}>
        Delete block
      </Button>
    </aside>
  );
}
