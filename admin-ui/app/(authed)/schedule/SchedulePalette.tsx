"use client";

import type { CSSProperties, ReactNode } from "react";
import { cn } from "@/lib/utils";
import { brushesEqual, personaSwatchClassName, type Brush } from "./schedule-grid-model";
import type { RosterPersonaDto } from "./types";

export interface SchedulePaletteProps {
  personas: readonly RosterPersonaDto[];
  selectedBrush: Brush | null;
  onSelectBrush: (brush: Brush | null) => void;
}

const SWATCH_BASE = "h-4 w-4 shrink-0 rounded-[3px] border border-line";

/** The hatched swatch that marks the music-only brush as visually distinct from any persona brush
 * (SPEC F94.3: "the music-only brush visually distinct e.g. hatched/muted") — a diagonal stripe
 * pattern over `--surface-2`/`--line`, reused verbatim by `ScheduleGrid`'s own music-only cells so
 * the palette swatch and the painted grid cells read as the same thing. */
export const MUSIC_HATCH_STYLE: CSSProperties = {
  backgroundColor: "var(--surface-2)",
  backgroundImage: "repeating-linear-gradient(45deg, var(--line) 0, var(--line) 2px, transparent 2px, transparent 7px)",
};

const BRUSH_BUTTON_BASE =
  "flex h-10 items-center gap-2 rounded-[6px] border px-3 text-[0.82rem] font-semibold transition-colors duration-[120ms] ease-out focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent";

function brushButtonClasses(isSelected: boolean): string {
  return cn(
    BRUSH_BUTTON_BASE,
    isSelected ? "border-accent bg-accent/10 text-ink" : "border-line bg-surface text-ink hover:bg-surface-2"
  );
}

/**
 * The roster palette (STORY-248, SPEC F94.3): one brush button per persona, plus the music-only
 * brush and the clear ("gap") brush. Clicking the ALREADY-selected brush deselects it back to
 * `null` — this is what puts the editor back into "inspect" mode, where clicking a painted block
 * on the grid opens its side panel instead of repainting it (`ScheduleEditor`'s own remarks own
 * that mode-switch rule; this component only owns the toggle gesture). At most one brush is ever
 * selected — there is no multi-select here, painting always applies exactly one value.
 */
export function SchedulePalette({ personas, selectedBrush, onSelectBrush }: SchedulePaletteProps): ReactNode {
  function toggle(brush: Brush): void {
    onSelectBrush(brushesEqual(selectedBrush, brush) ? null : brush);
  }

  return (
    <div role="group" aria-label="Roster palette" className="flex flex-wrap items-center gap-2">
      {personas.map((persona) => {
        const brush: Brush = { kind: "persona", personaId: persona.id, name: persona.name };
        const isSelected = brushesEqual(selectedBrush, brush);
        return (
          <button
            key={persona.id}
            type="button"
            aria-label={persona.name}
            aria-pressed={isSelected}
            className={brushButtonClasses(isSelected)}
            onClick={() => toggle(brush)}
          >
            <span className={cn(SWATCH_BASE, personaSwatchClassName(persona.id))} aria-hidden="true" />
            {persona.name}
          </button>
        );
      })}

      <button
        type="button"
        aria-label="Music only"
        aria-pressed={brushesEqual(selectedBrush, { kind: "music" })}
        className={brushButtonClasses(brushesEqual(selectedBrush, { kind: "music" }))}
        onClick={() => toggle({ kind: "music" })}
      >
        <span className={cn(SWATCH_BASE)} style={MUSIC_HATCH_STYLE} aria-hidden="true" />
        Music only
      </button>

      <button
        type="button"
        aria-label="Clear"
        aria-pressed={brushesEqual(selectedBrush, { kind: "clear" })}
        className={brushButtonClasses(brushesEqual(selectedBrush, { kind: "clear" }))}
        onClick={() => toggle({ kind: "clear" })}
      >
        <span className={cn(SWATCH_BASE, "bg-surface")} aria-hidden="true" />
        Clear
      </button>
    </div>
  );
}
