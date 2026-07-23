"use client";

import { useEffect, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { EmptyState } from "@/components/ui/empty-state";
import { cn } from "@/lib/utils";
import { usePersistedState } from "@/lib/use-persisted-state";
import type { LibraryDto } from "@/lib/library";
import { CatalogToolbar } from "./CatalogToolbar";
import { ColumnsToggle } from "./ColumnsToggle";
import { NeverPlayControl } from "./NeverPlayControl";
import {
  CATALOG_COLUMN_VISIBILITY_STORAGE_KEY,
  formatBpmCell,
  formatEnergyCell,
  formatYearCell,
  parseVisibleColumns,
  type OptionalCatalogColumn,
} from "./columnVisibility";
import type { AdminMediaDto, BulkFilter, Pagination } from "./types";

export interface CatalogTableProps {
  media: AdminMediaDto[];
  pagination: Pagination;
  libraries: LibraryDto[];
  bulkFilter: BulkFilter;
  /** Whether any search/filter field (other than pagination) is active — drives by-filter mode and which EmptyState renders. */
  filterActive: boolean;
  /** Where the EmptyState's "Clear filters" CTA sends the operator. */
  clearFiltersHref: string;
}

function formatDuration(durationMs: number | null): string {
  return durationMs !== null ? `${Math.round(durationMs / 1000)}s` : "—";
}

/** True when any of the nine bulk-endpoint filter dimensions is set — the by-filter bulk toolbar's
 * own activation condition (SPEC F28.11, widened by F52.5 to the three exact-match fields).
 * Deliberately independent of the browse-only never-play filter: F33.7 keeps rating state
 * standalone from the bulk write endpoints, which take no `neverPlay` filter field at all, so a
 * `?never-play=true` browse alone must never enable by-filter bulk mode — that would blast-radius
 * a "matching" count the bulk endpoint can't actually honor.
 *
 * String fields (state/artist/genre/q/artistExact/albumExact) treat `""` the same as `null` — the
 * catalog filter form always submits these as present-but-empty fields on a bare submit, and
 * page.tsx's `bulkFilter` passes that `""` through verbatim (byte-compatible with the retired
 * Bulk*Control shape, F28.11). Without this guard, submitting the filter form with nothing entered
 * would light up the by-filter toolbar over the *entire* unfiltered catalog (Q7 review Finding 1
 * regression) — this mirrors the `!== ""` guards page.tsx's own `isFilterActive` already applies
 * to those same fields. `artistExact`/`albumExact`/`genresExact` are also optional (not just
 * nullable, SPEC F52.5's types.ts note) so `undefined` reads the same as "not set".
 */
function hasBulkFilter(filter: BulkFilter): boolean {
  return (
    (filter.state !== null && filter.state !== "") ||
    (filter.artist !== null && filter.artist !== "") ||
    (filter.genre !== null && filter.genre !== "") ||
    filter.libraryId !== null ||
    (filter.q !== null && filter.q !== "") ||
    filter.eligible !== null ||
    (filter.artistExact !== null && filter.artistExact !== undefined && filter.artistExact !== "") ||
    (filter.albumExact !== null && filter.albumExact !== undefined && filter.albumExact !== "") ||
    (filter.genresExact !== undefined && filter.genresExact.length > 0)
  );
}

/** Never-play badge (SPEC F33.12) — flagged rows only, visually distinct from the plain Yes/No
 * eligibility cell so both states can coexist and read separately on one row. */
function NeverPlayBadge(): ReactNode {
  return (
    <span className="inline-flex w-fit items-center rounded-[3px] border border-accent-2 px-1.5 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-2">
      Never play
    </span>
  );
}

/** Moods cell (SPEC F86.8) — one bordered chip per mood tag, the shipped source-tag chip style
 * (design-aesthetic: "3px-radius bordered chip for source tags"), same visual language as
 * NeverPlayBadge and the filter chips above. Renders nothing (an empty cell, not an em-dash) for
 * a `null`/absent/empty moods row — the tagger hasn't reached it, and an empty cell reads as
 * "nothing here yet" without implying a missing measurement the way BPM/Energy's em-dash does. */
function MoodTags({ moods }: { moods: string[] | null | undefined }): ReactNode {
  if (moods === null || moods === undefined || moods.length === 0) return null;
  return (
    <ul aria-label="Moods" className="flex flex-wrap gap-1">
      {moods.map((mood) => (
        <li
          key={mood}
          className="inline-flex w-fit items-center rounded-[3px] border border-accent-2 px-1.5 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-2"
        >
          {mood}
        </li>
      ))}
    </ul>
  );
}

const HEADER_CELL = "py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";
const HEADER_CELL_RIGHT =
  "py-2 pr-3 text-right text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";

/**
 * The catalog's selectable track table + contextual bulk toolbar (SPEC F28.11, STORY-089).
 * Rows carry a checkbox; a header checkbox selects every row on this page (per-page
 * select-all — selection never spans pages). The toolbar appears whenever the selection is
 * non-empty, or the selection is empty and a filter is active (by-filter mode, matching the
 * retired Bulk*Control components' always-on behavior).
 *
 * A fresh `media` array — from a new filter/page or a post-mutation `router.refresh()` —
 * RECONCILES the selection rather than clearing it: any selected id no longer present in the new
 * array (paged/filtered away, or the row it names is gone) drops out; ids still present stay
 * selected. This is load-bearing for CatalogToolbar's selection-mode partial-failure contract
 * (Q7 review, Finding 1 regression fix): `onOutcome` narrows the selection down to just the
 * failed row ids *before* `router.refresh()` lands a new `media` array, so succeeded rows
 * (already dropped from the selection at that point) leave, and failed rows (still selected,
 * and still present post-refresh) stay checked for the operator to retry. Plain page/filter
 * navigation also benefits: a selection surviving into rows still on screen is more useful than
 * silently discarding it.
 */
export function CatalogTable({
  media,
  pagination,
  libraries,
  bulkFilter,
  filterActive,
  clearFiltersHref,
}: CatalogTableProps): ReactNode {
  const router = useRouter();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [toolbarBusy, setToolbarBusy] = useState(false);
  // Optimistic never-play overrides (SPEC F33.12): a restore/X toggle folds its PUT response in
  // here immediately (F33.11 "no refetch" posture) so the badge clears without waiting on the
  // network round trip started below. Reset whenever a fresh `media` array lands — a new
  // filter/page fetch (or this same toggle's own `router.refresh()`) already carries server truth.
  const [neverPlayOverrides, setNeverPlayOverrides] = useState<Map<string, boolean>>(new Map());

  // Year/BPM/Energy/Moods column visibility (SPEC F49.3, F86.8) — persisted in localStorage,
  // default hidden (empty array). Existing columns are not toggleable this phase.
  const [visibleColumns, setVisibleColumns] = usePersistedState<OptionalCatalogColumn[]>(
    CATALOG_COLUMN_VISIBILITY_STORAGE_KEY,
    [],
    parseVisibleColumns
  );
  const showYear = visibleColumns.includes("year");
  const showBpm = visibleColumns.includes("bpm");
  const showEnergy = visibleColumns.includes("energy");
  const showMoods = visibleColumns.includes("moods");

  function toggleColumn(column: OptionalCatalogColumn): void {
    setVisibleColumns(
      visibleColumns.includes(column)
        ? visibleColumns.filter((c) => c !== column)
        : [...visibleColumns, column]
    );
  }

  useEffect(() => {
    setSelected((prev) => new Set([...prev].filter((id) => media.some((m) => m.mediaId === id))));
    setNeverPlayOverrides(new Map());
  }, [media]);

  if (media.length === 0) {
    return filterActive ? (
      <EmptyState
        className="mt-6"
        title="No tracks match this filter"
        reason="Try a different search, or clear the filters to see the whole catalog."
        cta={{ label: "Clear filters", href: clearFiltersHref }}
      />
    ) : (
      <EmptyState
        className="mt-6"
        title="The catalog is empty"
        reason="Tracks appear here once the media library has been scanned."
      />
    );
  }

  const allSelected = selected.size > 0 && selected.size === media.length;
  // Independent of `filterActive` (which also reflects the browse-only never-play filter, F33.10)
  // — see hasBulkFilter's comment for why bulk mode must stay gated on the six bulk-filter fields.
  const toolbarVisible = selected.size > 0 || hasBulkFilter(bulkFilter);
  const selectedMedia = media.filter((m) => selected.has(m.mediaId));

  function toggleRow(mediaId: string): void {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(mediaId)) {
        next.delete(mediaId);
      } else {
        next.add(mediaId);
      }
      return next;
    });
  }

  function toggleSelectAll(): void {
    setSelected((prev) => (prev.size === media.length ? new Set() : new Set(media.map((m) => m.mediaId))));
  }

  /** Folds a successful restore/never-play PUT into local state, then refreshes so a
   * `?never-play=true` filtered view drops a just-restored row (SPEC F33.10, F33.12 AC3). */
  function handleNeverPlayChange(mediaId: string, neverPlay: boolean): void {
    setNeverPlayOverrides((prev) => new Map(prev).set(mediaId, neverPlay));
    router.refresh();
  }

  return (
    <div className="mt-6">
      {toolbarVisible && (
        <CatalogToolbar
          selectedMedia={selectedMedia}
          totalMatchingRows={pagination.total}
          filter={bulkFilter}
          libraries={libraries}
          busy={toolbarBusy}
          onBusyChange={setToolbarBusy}
          onOutcome={setSelected}
        />
      )}

      <div className="mb-3 flex justify-end">
        <ColumnsToggle visible={visibleColumns} onToggle={toggleColumn} />
      </div>

      {/* AC2 (SPEC F28.13): the table scrolls sideways inside this container at
          390px — the page body itself never does. */}
      <div className="overflow-x-auto">
        <table className="w-full border-collapse text-[0.85rem]">
          <thead>
            <tr className="border-b-2 border-line text-left">
              <th scope="col" className="w-9 py-2 pr-2">
                <span className="flex min-h-10 items-center">
                  <input
                    type="checkbox"
                    aria-label="Select all rows on this page"
                    checked={allSelected}
                    onChange={toggleSelectAll}
                    disabled={toolbarBusy}
                  />
                </span>
              </th>
              <th scope="col" className={HEADER_CELL}>
                Title
              </th>
              <th scope="col" className={HEADER_CELL}>
                Artist
              </th>
              <th scope="col" className={HEADER_CELL}>
                Genre
              </th>
              <th scope="col" className={HEADER_CELL}>
                State
              </th>
              <th scope="col" className={HEADER_CELL}>
                Eligible
              </th>
              <th scope="col" className={HEADER_CELL_RIGHT}>
                Score
              </th>
              <th scope="col" className={HEADER_CELL}>
                Rating
              </th>
              <th scope="col" className={HEADER_CELL_RIGHT}>
                Duration
              </th>
              {/* Year/BPM/Energy (SPEC F49.3) — hidden by default, toggled via ColumnsToggle. */}
              {showYear && (
                <th scope="col" className={HEADER_CELL_RIGHT}>
                  Year
                </th>
              )}
              {showBpm && (
                <th scope="col" className={HEADER_CELL_RIGHT}>
                  BPM
                </th>
              )}
              {showEnergy && (
                <th scope="col" className={HEADER_CELL_RIGHT}>
                  Energy
                </th>
              )}
              {/* Moods (SPEC F86.8) — hidden by default, toggled via ColumnsToggle. */}
              {showMoods && (
                <th scope="col" className={HEADER_CELL}>
                  Moods
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {media.map((item) => {
              const isSelected = selected.has(item.mediaId);
              const neverPlay = neverPlayOverrides.get(item.mediaId) ?? item.neverPlay;
              return (
                <tr
                  key={item.mediaId}
                  className={cn("border-b border-line last:border-b-0", isSelected && "bg-accent/5")}
                >
                  <td className="py-2 pr-2">
                    <span className="flex min-h-10 items-center">
                      <input
                        type="checkbox"
                        aria-label={`Select ${item.title ?? item.mediaId}`}
                        checked={isSelected}
                        onChange={() => toggleRow(item.mediaId)}
                        disabled={toolbarBusy}
                      />
                    </span>
                  </td>
                  <td className="py-2 pr-3 text-ink">
                    <Link href={`/catalog/${item.mediaId}`} className="hover:underline">
                      {item.title}
                    </Link>
                  </td>
                  <td className="py-2 pr-3 text-mute">{item.artist}</td>
                  <td className="py-2 pr-3 text-mute">{item.genre}</td>
                  <td className="py-2 pr-3 text-mute">{item.state}</td>
                  <td className="py-2 pr-3">
                    <span aria-label={item.eligible ? "eligible" : "ineligible"}>{item.eligible ? "Yes" : "No"}</span>
                  </td>
                  <td className="py-2 pr-3 text-right tabular-nums text-ink">{item.score}</td>
                  <td className="py-2 pr-3">
                    <div className="flex items-center gap-2">
                      {neverPlay && <NeverPlayBadge />}
                      <NeverPlayControl
                        mediaId={item.mediaId}
                        neverPlay={neverPlay}
                        onChange={(next) => handleNeverPlayChange(item.mediaId, next)}
                      />
                    </div>
                  </td>
                  <td className="py-2 pr-3 text-right tabular-nums text-ink">{formatDuration(item.durationMs)}</td>
                  {showYear && (
                    <td className="py-2 pr-3 text-right tabular-nums text-ink">{formatYearCell(item.year)}</td>
                  )}
                  {showBpm && (
                    <td className="py-2 pr-3 text-right tabular-nums text-ink">{formatBpmCell(item.bpm)}</td>
                  )}
                  {showEnergy && (
                    <td className="py-2 pr-3 text-right tabular-nums text-ink">{formatEnergyCell(item.trackEnergy)}</td>
                  )}
                  {showMoods && (
                    <td className="py-2 pr-3">
                      <MoodTags moods={item.moods} />
                    </td>
                  )}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
