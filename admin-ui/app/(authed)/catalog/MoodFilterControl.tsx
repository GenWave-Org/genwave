"use client";

import { useState, type ChangeEvent, type ReactNode } from "react";
import { MOOD_VOCABULARY } from "./moodVocabulary";

export interface MoodFilterControlProps {
  /** Currently-picked mood(s) from the URL (SPEC F86.8) — `?mood-exact=` values already active. */
  initialValues: string[];
}

/**
 * Mood filter field (SPEC F86.8) — a plain named multi-select inside the catalog's existing
 * `<form method="get">` filter bar, submitting the repeatable `mood-exact` param exactly the way
 * the genre facet picker's multi-select does (`FacetFilterControl`'s `multiple` mode). Composes
 * with every other active filter by construction: it's just one more named field in the same
 * form, so a native GET submit carries it alongside artist/genre/year/etc. rather than replacing
 * them (the catalog's existing filter-bar contract).
 *
 * Unlike artist/album/genre, this control issues NO fetch. The mood vocabulary is fixed
 * (`MoodVocabulary`, SPEC F85.1) — a fixed vocabulary needs no discovery request, so the term
 * list is mirrored here as a static constant (`moodVocabulary.ts`), pinned to the C# source by a
 * parity spec (`catalog-mood-vocabulary-parity.spec.ts`) rather than by a runtime round trip.
 */
export function MoodFilterControl({ initialValues }: MoodFilterControlProps): ReactNode {
  const [values, setValues] = useState<string[]>(initialValues);

  function handleChange(e: ChangeEvent<HTMLSelectElement>): void {
    setValues(Array.from(e.currentTarget.selectedOptions).map((opt) => opt.value));
  }

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor="mood-exact" className="text-[0.82rem] font-semibold text-mute">
        Mood
      </label>
      <select
        id="mood-exact"
        name="mood-exact"
        multiple
        size={4}
        aria-label="Mood is exactly"
        value={values}
        onChange={handleChange}
        className="h-9 min-w-[10rem] rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink"
      >
        {MOOD_VOCABULARY.map((term) => (
          <option key={term} value={term}>
            {term}
          </option>
        ))}
      </select>
    </div>
  );
}
