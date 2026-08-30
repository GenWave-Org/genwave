import type { ReactNode } from "react";
import { PurgeUnavailableAction } from "../_components/PurgeUnavailableAction";
import {
  GARDENER_KIND_EMPTY_LABELS,
  GARDENER_KIND_LABELS,
  type GardenerGroupDto,
  type GardenerKind,
} from "@/lib/gardener-api";
import { DuplicateGroupCard } from "./DuplicateGroupCard";
import { GardenerRow } from "./GardenerRow";

interface GardenerSectionProps {
  kind: GardenerKind;
  group: GardenerGroupDto;
  /** `GET /api/status`'s own per-kind OPEN total (SPEC F153.9) — `null` when the status fetch
   * itself failed, in which case the header falls back to this page's own row count. */
  openCount: number | null;
  onChanged: () => void;
}

/**
 * One kind's section (SPEC F153.10, STORY-374 AC9): a header naming the kind and its open count,
 * a per-kind empty state when nothing qualifies (LOW-2 — "Nothing here." read as generic; each
 * kind now names itself), the "Showing first N of M" flat-paging caveat when this page's own row
 * count for the kind is short of the status total (ORCHESTRATOR ruling 2 — rows are paged FLAT
 * before grouping, so a page's own count for one kind can legitimately be less than that kind's
 * real total), and either a flat row list (every kind but near_duplicate) or one
 * {@link DuplicateGroupCard} per duplicate group (near_duplicate only — STORY-376 AC6).
 *
 * T378 review LOW-5/LOW-B: the duplicate-group branch renders from `group.duplicateGroups` — never
 * a `kind === "near_duplicate"` check alone — because `duplicateGroups` (not `findings.length`) is
 * the actual data that branch draws from. A group with no `groupKey` is filtered out BEFORE
 * `hasDuplicateGroups` is computed (not inside the render map, LOW-B's own fix) — Keep this one's
 * whole point is "mark the OTHER members of THIS group ineligible", meaningless without a real
 * group identity, and filtering only at render time left `hasDuplicateGroups` true even when every
 * group had been filtered away, rendering an empty header with nothing under it. Filtering first
 * means an all-null set falls through to the flat row list (the SAME fallback every non-
 * near_duplicate kind renders) instead. Never reachable from the real backend today — a
 * near_duplicate finding always carries its own `group_key` — but this keeps a malformed/future
 * response from rendering a Keep-this-one button (or an empty shell) with no group behind it.
 */
export function GardenerSection({ kind, group, openCount, onChanged }: GardenerSectionProps): ReactNode {
  const label = GARDENER_KIND_LABELS[kind];
  const rowCount = group.findings.length;
  const displayCount = openCount ?? rowCount;
  const showingFewer = openCount !== null && rowCount < openCount;
  const duplicateGroups = group.duplicateGroups.filter((duplicateGroup) => duplicateGroup.groupKey !== null);
  const hasDuplicateGroups = duplicateGroups.length > 0;

  return (
    <section aria-label={label} className="rounded-[6px] border border-line bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-display text-[1.05rem] font-semibold text-ink">
          {label} <span className="text-[0.85rem] font-normal text-mute">· {displayCount} open</span>
        </h2>
        {kind === "dead_file" && rowCount > 0 && (
          <PurgeUnavailableAction title="Purge unavailable" triggerLabel="Purge unavailable…" onPurged={onChanged} />
        )}
      </div>

      {showingFewer && (
        <p className="mt-1 text-[0.75rem] text-mute">
          Showing first {rowCount} of {openCount}
        </p>
      )}

      {rowCount === 0 && <p className="mt-3 text-[0.85rem] text-mute">{GARDENER_KIND_EMPTY_LABELS[kind]}</p>}

      {rowCount > 0 && hasDuplicateGroups && (
        <div className="mt-3 space-y-3">
          {duplicateGroups.map((duplicateGroup) => (
            <DuplicateGroupCard key={duplicateGroup.groupKey} group={duplicateGroup} onChanged={onChanged} />
          ))}
        </div>
      )}

      {rowCount > 0 && !hasDuplicateGroups && (
        <div className="mt-3 divide-y divide-line">
          {group.findings.map((finding) => (
            <GardenerRow key={finding.id} kind={kind} finding={finding} onChanged={onChanged} />
          ))}
        </div>
      )}
    </section>
  );
}
