"use client";

import { useId, useState, type KeyboardEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/** A catalog row offered as a bed candidate — id is numeric (bedMediaId on the wire, F27.3). */
export interface BedCandidate {
  mediaId: number;
  title: string | null;
  artist: string | null;
}

interface BedPickerProps {
  /** Currently selected bed, or null when none is chosen (optional field, F27.9). */
  selected: BedCandidate | null;
  onSelect: (candidate: BedCandidate) => void;
  onClear: () => void;
  disabled: boolean;
}

/** Shape of a GET /api/media row — only the fields the bed picker renders. */
interface MediaSearchRow {
  mediaId: string;
  title: string | null;
  artist: string | null;
}

type SearchStatus =
  | { kind: "idle" }
  | { kind: "pending" }
  | { kind: "error"; message: string };

function toBedCandidate(row: MediaSearchRow): BedCandidate {
  return { mediaId: Number(row.mediaId), title: row.title, artist: row.artist };
}

function candidateLabel(candidate: BedCandidate): string {
  const name = candidate.title ?? `#${candidate.mediaId}`;
  return candidate.artist !== null ? `${name} — ${candidate.artist}` : name;
}

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 w-full rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * Optional bed picker for the Generate form (SPEC F27.4/F27.9, restyled Q10 per F28.9/F28.10).
 * Searches the MAIN catalog scope (no `library-id`) via the shipped GET /api/media?q= browse —
 * a bed is a main-rotation jingle, not a row from the safe library itself. Search stays an
 * explicit-trigger action (the Search button) — no on-keystroke fetch invented, matching the
 * shipped wire semantics. Once results land, the input behaves as an ARIA 1.2 combobox:
 * ArrowUp/ArrowDown move `aria-activedescendant` across the result listbox, Enter selects the
 * highlighted row (and is stopped from bubbling to the outer Generate `<form>`, which this
 * component is nested inside), Escape closes the listbox without discarding the typed query.
 * Mouse users can still click a row's own Select button. Selection reports the candidate's
 * numeric id up to the parent, which submits it as `bedMediaId` (F27.3).
 */
export function BedPicker({ selected, onSelect, onClear, disabled }: BedPickerProps): ReactNode {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<BedCandidate[]>([]);
  const [status, setStatus] = useState<SearchStatus>({ kind: "idle" });
  const [activeIndex, setActiveIndex] = useState(-1);
  const listboxId = useId();

  const isOpen = results.length > 0;

  async function handleSearch(): Promise<void> {
    if (query.trim() === "") {
      setResults([]);
      return;
    }

    setStatus({ kind: "pending" });
    setActiveIndex(-1);
    try {
      const resp = await fetch(`/api/media?q=${encodeURIComponent(query)}`);
      if (!resp.ok) {
        setStatus({ kind: "error", message: `Search failed (${resp.status})` });
        return;
      }
      const rows = (await resp.json()) as MediaSearchRow[];
      setResults(rows.map(toBedCandidate));
      setStatus({ kind: "idle" });
    } catch {
      setStatus({ kind: "error", message: "Network error — check your connection" });
    }
  }

  function handleSelect(candidate: BedCandidate): void {
    onSelect(candidate);
    setResults([]);
    setQuery("");
    setActiveIndex(-1);
    setStatus({ kind: "idle" });
  }

  function handleKeyDown(e: KeyboardEvent<HTMLInputElement>): void {
    if (!isOpen) return;

    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActiveIndex((prev) => (prev + 1) % results.length);
      return;
    }
    if (e.key === "ArrowUp") {
      e.preventDefault();
      setActiveIndex((prev) => (prev <= 0 ? results.length - 1 : prev - 1));
      return;
    }
    if (e.key === "Enter") {
      // The listbox is open (guarded above) — always stop Enter here so it can't leak through
      // and submit the outer Generate <form> this picker is nested inside, even before the
      // operator has arrow-navigated to a specific row.
      e.preventDefault();
      if (activeIndex < 0) return;
      const candidate = results[activeIndex];
      if (candidate !== undefined) handleSelect(candidate);
      return;
    }
    if (e.key === "Escape") {
      e.preventDefault();
      setResults([]);
      setActiveIndex(-1);
    }
  }

  const activeOptionId = activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined;

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor="bed-search" className={FIELD_LABEL_CLASSES}>
        Bed (optional)
      </label>

      {selected !== null ? (
        <div className="flex items-center gap-2 rounded-[6px] border border-line bg-surface-2 px-3 py-2 text-[0.85rem] text-ink">
          <span className="flex-1">{candidateLabel(selected)}</span>
          <Button type="button" variant="secondary" onClick={onClear} disabled={disabled}>
            Clear
          </Button>
        </div>
      ) : (
        <div className="relative">
          <div className="flex gap-2">
            <input
              id="bed-search"
              type="search"
              role="combobox"
              aria-expanded={isOpen}
              aria-controls={listboxId}
              aria-autocomplete="list"
              aria-activedescendant={activeOptionId}
              value={query}
              onChange={(e) => setQuery(e.currentTarget.value)}
              onKeyDown={handleKeyDown}
              disabled={disabled}
              className={FIELD_INPUT_CLASSES}
            />
            <Button
              type="button"
              variant="secondary"
              onClick={() => { void handleSearch(); }}
              disabled={disabled}
            >
              Search
            </Button>
          </div>

          {status.kind === "error" && (
            <p role="alert" aria-live="assertive" className="mt-1.5 text-[0.78rem] text-danger">
              {status.message}
            </p>
          )}

          {isOpen && (
            <ul
              id={listboxId}
              role="listbox"
              aria-label="Bed search results"
              className="absolute z-10 mt-1.5 max-h-56 w-full overflow-auto rounded-[6px] border border-line bg-surface py-1"
            >
              {results.map((candidate, index) => (
                <li
                  key={candidate.mediaId}
                  id={`${listboxId}-option-${index}`}
                  role="option"
                  aria-selected={index === activeIndex}
                  className={cn(
                    "flex items-center justify-between gap-2 px-3 py-1.5 text-[0.85rem] text-ink",
                    index === activeIndex && "bg-accent/10"
                  )}
                >
                  <span>{candidateLabel(candidate)}</span>
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => handleSelect(candidate)}
                    disabled={disabled}
                  >
                    Select
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
