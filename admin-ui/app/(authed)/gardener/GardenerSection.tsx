"use client";

import type { ReactNode } from "react";
import { useRouter } from "next/navigation";
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
   * itself failed, in which case the header falls back to {@link total}, EXCEPT for
   * `near_duplicate` (T387 review LOW-2, RULED): `openCount` is a ROW count, but `near_duplicate`'s
   * own `total` is a GROUP count (STORY-382 AC6/AC8) — falling back to it there would silently swap
   * units, showing a group count as though it were an open-row count. That one kind suppresses the
   * header count entirely instead (honest beats unit-swapped); every other kind's `total` is
   * row-scoped, same unit as `openCount`, so the fallback stays correct there. The old "Showing
   * first N of M" flat-paging caveat this header used to carry when the two disagreed is GONE
   * (SPEC F153.10 rider 2026-08-31) — a real pager (`Pager`) replaces it. */
  openCount: number | null;
  /** The active tab's own exact total (the kind-scoped `GardenerFindingsResponse.total`, STORY-382
   * AC6/AC8) — the header's fallback source when status failed, and what gates the dead_file Purge
   * trigger below: a beyond-end page can legitimately render zero rows while the kind itself still
   * has dead files to purge, so gating on `total` (not this page's own row count) stays correct. */
  total: number;
}

/**
 * One kind's section — the tab strip's own content pane (SPEC F153.10 rider 2026-08-31; STORY-381/
 * 382; PLAN T387, gh-#654/#655/#657): a header naming the kind and its open count, a per-kind empty
 * state when nothing qualifies (LOW-2 — "Nothing here." read as generic; each kind names itself),
 * and either a flat row list (every kind but near_duplicate) or one {@link DuplicateGroupCard} per
 * duplicate group (near_duplicate only — STORY-376 AC6, STORY-383 AC4 whole-cluster rendering).
 * Exactly ONE kind renders per page load now — the tab strip (`GardenerTabs`) owns which.
 *
 * This is now the page's own "use client" boundary: `page.tsx` (a Server Component) renders this
 * directly, mirroring `catalog/CatalogTable.tsx`'s own split — a top-level client component that
 * owns `useRouter()` and threads a `router.refresh()` closure down to every verb, rather than a
 * closure passed in as a prop from the server (which RSC cannot serialize). `GardenerView`'s own
 * client LoadState/fetch-on-mount — the gh-#654 defect — retires with this: every row verb still
 * re-fetches on success, but by asking Next.js to re-render this Server Component, not by holding
 * a second client-side copy of the queue. Purge stays dead_file-tab-only, now carrying the gh-#655
 * verb-object label ("Purge dead tracks…"/"Purge dead tracks") — the old "Purge unavailable…" read
 * as a status, never naming what the click actually does.
 *
 * T378 review LOW-5/LOW-B (carried forward verbatim): the duplicate-group branch renders from
 * `group.duplicateGroups` — never a `kind === "near_duplicate"` check alone — and a group with no
 * `groupKey` is filtered out BEFORE `hasDuplicateGroups` is computed, so a malformed/future
 * response falls through to the flat row list instead of an empty shell with nothing under it.
 */
export function GardenerSection({ kind, group, openCount, total }: GardenerSectionProps): ReactNode {
  const router = useRouter();
  const onChanged = (): void => router.refresh();

  const label = GARDENER_KIND_LABELS[kind];
  const rowCount = group.findings.length;
  // LOW-2 (RULED): near_duplicate's own `total` is a GROUP count, not a ROW count like `openCount`
  // — falling back to it would silently swap units, so that one kind suppresses the count instead.
  const displayCount: number | null = openCount ?? (kind === "near_duplicate" ? null : total);
  const duplicateGroups = group.duplicateGroups.filter((duplicateGroup) => duplicateGroup.groupKey !== null);
  const hasDuplicateGroups = duplicateGroups.length > 0;

  return (
    <section aria-label={label} className="rounded-[6px] border border-line bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-display text-[1.05rem] font-semibold text-ink">
          {label}
          {displayCount !== null && (
            <span className="text-[0.85rem] font-normal text-mute"> · {displayCount} open</span>
          )}
        </h2>
        {kind === "dead_file" && total > 0 && (
          <PurgeUnavailableAction title="Purge dead tracks" triggerLabel="Purge dead tracks…" onPurged={onChanged} />
        )}
      </div>

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
