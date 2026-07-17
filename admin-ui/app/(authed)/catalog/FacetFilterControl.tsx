"use client";

import { useEffect, useState, type ChangeEvent, type ReactNode } from "react";
import type { FacetOption } from "./types";

function isFacetOptionList(raw: unknown): raw is FacetOption[] {
  return (
    Array.isArray(raw) &&
    raw.every((entry): entry is FacetOption => {
      if (typeof entry !== "object" || entry === null) return false;
      const record = entry as Record<string, unknown>;
      return typeof record["value"] === "string" && typeof record["count"] === "number";
    })
  );
}

/**
 * Merges the fetched facet list with any value(s) already picked but missing from it (the F54.2
 * voice-dropdown precedent) — a bookmarked exact filter, or a value belonging to a library that
 * just left scope, must still render as a selected option rather than silently vanish. Synthetic
 * entries carry `count: 0` so they're visually distinguishable from a real facet row.
 */
function mergeWithPicked(options: FacetOption[], picked: string[]): FacetOption[] {
  const known = new Set(options.map((o) => o.value.toLowerCase()));
  const extra = picked
    .filter((v) => v !== "" && !known.has(v.toLowerCase()))
    .map((value) => ({ value, count: 0 }));
  return [...options, ...extra];
}

type FacetFetchStatus = { kind: "loading" } | { kind: "loaded"; options: FacetOption[] } | { kind: "error" };

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES = "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink";

export interface FacetFilterControlProps {
  /** Facet field name sent to `GET /api/media/facets?field=`. */
  field: "artist" | "album" | "genre";
  /** Visible group label, e.g. "Artist". */
  label: string;
  /** Query param name the exact picker (or its fallback input) submits — `artist-exact` /
   * `album-exact` / `genre-exact`. */
  exactParamName: string;
  /** Substring param name for the shipped free-text mode — undefined when the field has no
   * substring counterpart on the backend (album; SPEC F52.3 — `q` covers it instead). */
  substringParamName?: string;
  /** True for genre (multi-select, OR-matched); false for artist/album (single-select). */
  multiple: boolean;
  /** Current substring value from the URL, when `substringParamName` is set. */
  initialSubstringValue?: string;
  /** Current exact-picked value(s) from the URL. */
  initialExactValues: string[];
  /** Scopes the facets fetch to a named library, mirroring the browse's own `?library-id=`. */
  libraryId?: string;
}

/**
 * Artist/album/genre filter field (SPEC F52.5) — an exact-match picker fed by
 * `GET /api/media/facets`, fetched once on mount (the VoiceControl discipline: fetch on mount,
 * never per keystroke). Artist and genre keep their shipped free-text substring input rendered
 * alongside the picker; album has none (`q` already searches album, F52.3), so it renders the
 * picker alone.
 *
 * The two modes are mutually exclusive by construction, not just convention: picking a facet
 * value clears the sibling substring field's state, and typing in the substring field clears the
 * picker's — so at most one of a field's two params is ever NON-EMPTY. That's the real safety net,
 * not exclusivity of the params themselves: both controls are plain named inputs inside the
 * catalog's existing `<form method="get">`, and a native GET submission serializes every named
 * input including the empty sibling — a facet pick emits `?artist=&artist-exact=Queen`, not just
 * `?artist-exact=Queen`. What makes that harmless happens downstream of this component: page.tsx's
 * `appendFilterParams`/`genreExactValues` strip a blank `*-exact` param before it ever reaches the
 * API, and ASP.NET Core's default `[FromQuery] string?` binding folds an empty substring param to
 * null (`ConvertEmptyStringToNull`) before the API's mutual-exclusion check runs — so that check
 * never sees both non-null, even though both params rode along on the wire (the F52.3 400 the API
 * guards against; mirrors YearFilterControl's decade/year-missing clearing). Don't delete either
 * "empty" guard believing the params are naturally exclusive — they aren't; the guards are.
 *
 * On fetch failure the picker degrades to a free-text input submitting the SAME exact param
 * (F52.5, the VoiceControl fallback discipline) — filtering never blocks. For artist/genre this
 * sits alongside the substring field, which already covers "keep filtering" on its own; for
 * album, whose only mode is the picker, that free-text input is the sole way to keep filtering.
 */
export function FacetFilterControl({
  field,
  label,
  exactParamName,
  substringParamName,
  multiple,
  initialSubstringValue,
  initialExactValues,
  libraryId,
}: FacetFilterControlProps): ReactNode {
  const [status, setStatus] = useState<FacetFetchStatus>({ kind: "loading" });
  const [substringValue, setSubstringValue] = useState(initialSubstringValue ?? "");
  const [exactValue, setExactValue] = useState(initialExactValues[0] ?? "");
  const [exactValues, setExactValues] = useState<string[]>(initialExactValues);

  useEffect(() => {
    let cancelled = false;

    async function loadFacets(): Promise<void> {
      try {
        const query = new URLSearchParams({ field });
        if (libraryId !== undefined) query.set("library-id", libraryId);
        const resp = await fetch(`/api/media/facets?${query.toString()}`);
        if (!resp.ok) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        const raw = (await resp.json()) as unknown;
        if (!isFacetOptionList(raw)) {
          if (!cancelled) setStatus({ kind: "error" });
          return;
        }
        if (!cancelled) setStatus({ kind: "loaded", options: raw });
      } catch {
        if (!cancelled) setStatus({ kind: "error" });
      }
    }

    void loadFacets();
    return () => {
      cancelled = true;
    };
  }, [field, libraryId]);

  function handleSubstringChange(e: ChangeEvent<HTMLInputElement>): void {
    const next = e.currentTarget.value;
    setSubstringValue(next);
    if (next !== "") {
      setExactValue("");
      setExactValues([]);
    }
  }

  function handleExactChange(e: ChangeEvent<HTMLSelectElement>): void {
    const next = e.currentTarget.value;
    setExactValue(next);
    if (next !== "") setSubstringValue("");
  }

  function handleExactMultiChange(e: ChangeEvent<HTMLSelectElement>): void {
    const next = Array.from(e.currentTarget.selectedOptions).map((opt) => opt.value);
    setExactValues(next);
    if (next.length > 0) setSubstringValue("");
  }

  /** The failure-mode input — same name/param the picker would have submitted, single-valued
   * even for genre (typing one exact genre back in is still strictly better than no filter). */
  function handleFallbackChange(e: ChangeEvent<HTMLInputElement>): void {
    const next = e.currentTarget.value;
    if (multiple) {
      setExactValues(next === "" ? [] : [next]);
    } else {
      setExactValue(next);
    }
    if (next !== "") setSubstringValue("");
  }

  const substringId = `${field}-contains`;
  const exactId = exactParamName;
  const noticeId = `${exactParamName}-notice`;

  if (status.kind === "error") {
    return (
      <div className="flex flex-col gap-1.5">
        <span className={FIELD_LABEL_CLASSES}>{label}</span>
        <div className="flex h-9 items-center gap-3">
          {substringParamName !== undefined && (
            <>
              <label htmlFor={substringId} className="sr-only">{`${label} contains`}</label>
              <input
                id={substringId}
                name={substringParamName}
                type="text"
                placeholder="Contains…"
                aria-label={`${label} contains`}
                value={substringValue}
                onChange={handleSubstringChange}
                className={FIELD_INPUT_CLASSES}
              />
            </>
          )}
          <label htmlFor={exactId} className="sr-only">{`${label} is exactly`}</label>
          <input
            id={exactId}
            name={exactParamName}
            type="text"
            placeholder="Is exactly…"
            aria-label={`${label} is exactly`}
            value={multiple ? (exactValues[0] ?? "") : exactValue}
            onChange={handleFallbackChange}
            aria-describedby={noticeId}
            className={FIELD_INPUT_CLASSES}
          />
        </div>
        <p id={noticeId} className="text-[0.78rem] text-mute">
          {`${label} list unavailable — type the exact value to filter${
            substringParamName !== undefined ? ', or use "Contains" instead' : ""
          }.`}
        </p>
      </div>
    );
  }

  const isLoading = status.kind === "loading";
  const pickedValues = multiple ? exactValues : exactValue !== "" ? [exactValue] : [];
  const options = status.kind === "loaded" ? mergeWithPicked(status.options, pickedValues) : [];

  return (
    <div className="flex flex-col gap-1.5">
      <span className={FIELD_LABEL_CLASSES}>{label}</span>
      <div className={multiple ? "flex items-start gap-3" : "flex h-9 items-center gap-3"}>
        {substringParamName !== undefined && (
          <>
            <label htmlFor={substringId} className="sr-only">{`${label} contains`}</label>
            <input
              id={substringId}
              name={substringParamName}
              type="text"
              placeholder="Contains…"
              aria-label={`${label} contains`}
              value={substringValue}
              onChange={handleSubstringChange}
              className={FIELD_INPUT_CLASSES}
            />
          </>
        )}
        <label htmlFor={exactId} className="sr-only">{`${label} is exactly`}</label>
        {multiple ? (
          <select
            id={exactId}
            name={exactParamName}
            multiple
            size={4}
            aria-label={`${label} is exactly`}
            value={exactValues}
            onChange={handleExactMultiChange}
            disabled={isLoading}
            className={`${FIELD_INPUT_CLASSES} min-w-[10rem]`}
          >
            {options.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.count > 0 ? `${opt.value} (${opt.count})` : opt.value}
              </option>
            ))}
          </select>
        ) : (
          <select
            id={exactId}
            name={exactParamName}
            aria-label={`${label} is exactly`}
            value={exactValue}
            onChange={handleExactChange}
            disabled={isLoading}
            className={`${FIELD_INPUT_CLASSES} w-fit`}
          >
            <option value="">{isLoading ? "Loading…" : "Is exactly…"}</option>
            {options.map((opt) => (
              <option key={opt.value} value={opt.value}>
                {opt.count > 0 ? `${opt.value} (${opt.count})` : opt.value}
              </option>
            ))}
          </select>
        )}
      </div>
    </div>
  );
}
