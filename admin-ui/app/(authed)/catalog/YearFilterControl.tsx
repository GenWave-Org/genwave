"use client";

import { useState, type ReactNode } from "react";

/** Decades offered in the control — the catalog's realistic release-year range. */
const DECADES = [1950, 1960, 1970, 1980, 1990, 2000, 2010, 2020];

export interface YearFilterControlProps {
  /** Decade value from the URL (e.g. "1970"), or undefined when no decade filter is active. */
  initialDecade?: string;
  /** year-missing value from the URL ("true" when active), or undefined otherwise. */
  initialYearMissing?: string;
}

/**
 * Decade / missing-year filter fields (SPEC F49.1, F49.4) — plain named inputs inside the
 * catalog's existing `<form method="get">` filter bar, so they submit exactly like the
 * state/artist/genre/eligible fields already do (no extra client fetch).
 *
 * Mutually exclusive client-side: picking one clears the other's field before submit, mirroring
 * the API's own "name at most one of year/decade/year-missing" 400 (F49.1) — an operator can
 * never assemble a request that trips it.
 */
export function YearFilterControl({ initialDecade, initialYearMissing }: YearFilterControlProps): ReactNode {
  const [decade, setDecade] = useState(initialDecade ?? "");
  const [yearMissing, setYearMissing] = useState(initialYearMissing === "true");

  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-[0.82rem] font-semibold text-mute">Release year</span>
      <div className="flex h-9 items-center gap-3">
        <label htmlFor="decade" className="sr-only">
          Decade
        </label>
        <select
          id="decade"
          name="decade"
          value={decade}
          onChange={(e) => {
            const next = e.currentTarget.value;
            setDecade(next);
            if (next !== "") setYearMissing(false);
          }}
          className="h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink"
        >
          <option value="">All years</option>
          {DECADES.map((d) => (
            <option key={d} value={d}>{`${d}s`}</option>
          ))}
        </select>

        <label htmlFor="year-missing" className="flex min-h-10 items-center gap-1.5 text-[0.85rem] text-ink">
          <input
            id="year-missing"
            name="year-missing"
            type="checkbox"
            value="true"
            checked={yearMissing}
            onChange={(e) => {
              const checked = e.currentTarget.checked;
              setYearMissing(checked);
              if (checked) setDecade("");
            }}
          />
          Missing year
        </label>
      </div>
    </div>
  );
}
