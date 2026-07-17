"use client";

import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { OPTIONAL_CATALOG_COLUMNS, columnLabel, type OptionalCatalogColumn } from "./columnVisibility";

export interface ColumnsToggleProps {
  visible: readonly OptionalCatalogColumn[];
  onToggle: (column: OptionalCatalogColumn) => void;
}

/**
 * Toolbar "Columns" control (SPEC F49.3) — shows/hides the Year/BPM/Energy columns, persisted by
 * the caller (CatalogTable) via `usePersistedState`. Follows BedPicker's disclosure pattern
 * (design-aesthetic): a trigger button plus an absolutely-positioned `--surface`/`--line` panel —
 * no popover/menu dependency added. The panel is `role="group"` (a checkbox group, not a menu),
 * so the trigger wires `aria-controls` to it — same as BedPicker's combobox does for its
 * listbox — rather than `aria-haspopup`, which implies menu semantics this panel doesn't have.
 * Closes on Escape or a click/focus outside, mirroring BedPicker's Escape handling.
 */
export function ColumnsToggle({ visible, onToggle }: ColumnsToggleProps): ReactNode {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const panelId = useId();

  useEffect(() => {
    if (!open) return;

    function handleOutsideEvent(e: Event): void {
      if (containerRef.current !== null && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === "Escape") {
        setOpen(false);
      }
    }

    // mousedown (pointer click-outside) and focusin (keyboard tab-away) both dismiss the panel —
    // a mouse user clicking elsewhere and a keyboard user tabbing past it should both close it.
    document.addEventListener("mousedown", handleOutsideEvent);
    document.addEventListener("focusin", handleOutsideEvent);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("mousedown", handleOutsideEvent);
      document.removeEventListener("focusin", handleOutsideEvent);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, [open]);

  return (
    <div ref={containerRef} className="relative">
      <Button
        type="button"
        variant="secondary"
        aria-expanded={open}
        aria-controls={panelId}
        onClick={() => setOpen((prev) => !prev)}
      >
        Columns
      </Button>

      {open && (
        <div
          id={panelId}
          role="group"
          aria-label="Toggle columns"
          className="absolute right-0 z-10 mt-1.5 flex flex-col gap-1.5 rounded-[6px] border border-line bg-surface p-3"
        >
          {OPTIONAL_CATALOG_COLUMNS.map((column) => (
            <label key={column} className="flex min-h-10 items-center gap-1.5 text-[0.85rem] text-ink">
              <input type="checkbox" checked={visible.includes(column)} onChange={() => onToggle(column)} />
              {columnLabel(column)}
            </label>
          ))}
        </div>
      )}
    </div>
  );
}
