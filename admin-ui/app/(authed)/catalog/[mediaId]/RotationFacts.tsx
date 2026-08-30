"use client";

import type { ReactNode } from "react";
import { formatDateStamp } from "@/lib/format-clock";

export interface RotationFactsProps {
  /** `library.media_rotation.play_count`; null/undefined (travels with the other two — never a
   * partial null) means the row has never aired. */
  plays: number | null | undefined;
  firstAiredAt: string | null | undefined;
  lastAiredAt: string | null | undefined;
  /** Test-only injection point for `formatDateStamp`'s own zone — production omits this and gets
   * the browser's local zone (the ProvenanceChip.tsx house idiom). */
  timeZone?: string;
}

/**
 * SPEC F149.5, STORY-368, PLAN T371 — the detail page's rotation-facts line: "N plays · First
 * aired &lt;date&gt; · Last aired &lt;date&gt;", or "Never aired" when the row carries no ledger
 * row at all.
 *
 * A small "use client" component (T371 review MED-1), not a plain function the server page calls
 * directly: `MediaDetailPage` is a Server Component (Node, UTC in the container), and every other
 * date surface in this codebase formats client-side in the browser's own zone (ProvenanceChip.tsx's
 * own idiom — `formatDateStamp` with no `timeZone` reads `Intl.DateTimeFormat`'s ambient zone,
 * which is only the OPERATOR's zone when the call itself runs in the browser). The server page
 * passes the raw ISO strings through as props; this component does the actual formatting once
 * hydrated.
 */
export function RotationFacts({ plays, firstAiredAt, lastAiredAt, timeZone }: RotationFactsProps): ReactNode {
  if (plays === null || plays === undefined) return "Never aired";

  const playsNoun = plays === 1 ? "play" : "plays";
  const first = firstAiredAt ? formatDateStamp(firstAiredAt, { timeZone }) : "—";
  const last = lastAiredAt ? formatDateStamp(lastAiredAt, { timeZone }) : "—";
  return `${plays} ${playsNoun} · First aired ${first} · Last aired ${last}`;
}
