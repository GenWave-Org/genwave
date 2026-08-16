import type { ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { formatDateStamp } from "@/lib/format-clock";

export interface ProvenanceChipProps {
  /** The provenance stamp — renders VERBATIM, never re-derived from `slug` even though it is
   * always equal to it today (a pack has no authored-in-place path): this IS the provenance
   * column, the same "read the real field, don't infer it" discipline every other chip in this
   * codebase follows. */
  importedFrom: string;
  importedAt: string;
  /** Test-only injection point for `formatDateStamp`'s own zone — production omits this and gets
   * the browser's local zone (the WardrobeClient/PersonasClient/SettingsForm house idiom). */
  timeZone?: string;
}

/**
 * "Installed · &lt;slug&gt; · &lt;date&gt;" (the db/25 pattern) — the ONE shared implementation
 * (PLAN T304 rider 3): `WardrobeClient` and `AvatarWardrobeClient` each carried their own
 * byte-identical copy of this exact chip (mirrors `Chip` itself, gh-#375's own extraction one
 * level down — only the visual pill styling was ever shared there; the "Installed · …" TEXT
 * composition is what THIS extraction shares); `IconWardrobeClient` would have been copy #3 had
 * this task minted its own.
 */
export function ProvenanceChip({ importedFrom, importedAt, timeZone }: ProvenanceChipProps): ReactNode {
  return <Chip>{`Installed · ${importedFrom} · ${formatDateStamp(importedAt, { timeZone })}`}</Chip>;
}
