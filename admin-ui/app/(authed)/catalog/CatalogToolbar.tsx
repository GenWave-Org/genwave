"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { IconButton } from "@/components/ui/icon-button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { setNeverPlay, voteTrack, type VoteDirection } from "@/lib/broadcast-api";
import type { LibraryDto } from "@/lib/library";
import { useRowPatch } from "@/lib/use-row-patch";
import { CloseIcon, RestoreIcon, VoteDownIcon, VoteUpIcon } from "../_components/icons";
import {
  ALL_REENRICH_FIELDS,
  REENRICH_FIELD_HINTS,
  REENRICH_FIELD_LABELS,
  type ReenrichField,
} from "./reenrichFields";
import type { AdminMediaDto, BulkFilter } from "./types";

/** At most this many single-row requests are in flight at once in selection mode — selection is
 * page-bounded (≤200 rows), so a small worker pool is enough (F28.11 review, Finding 1). */
const SELECTION_BATCH_CONCURRENCY = 5;

export interface CatalogToolbarProps {
  /** The selected rows on the current page (mediaId + version, for If-Match). Empty => by-filter
   * mode (toolbar only shows this way when a filter is active); non-empty => selection mode. */
  selectedMedia: AdminMediaDto[];
  /** Rows matching the current filter across every page (X-Pagination total) — the by-filter blast-radius figure. */
  totalMatchingRows: number;
  /** The current page's filter — sent verbatim to the three bulk endpoints in by-filter mode. */
  filter: BulkFilter;
  libraries: LibraryDto[];
  /** True while a confirm or a mutation is in flight — every button disables. */
  busy: boolean;
  onBusyChange: (busy: boolean) => void;
  /**
   * Called after every action settles, with the mediaIds that failed (empty on full success, in
   * both modes). The caller re-points its selection at exactly this set — succeeded rows leave
   * the selection, failed rows stay selected so the operator can retry them.
   */
  onOutcome: (failedMediaIds: Set<string>) => void;
}

interface RowOutcome {
  mediaId: string;
  ok: boolean;
  /** True when a reassign PATCH succeeded but the destination library is outside station scope. */
  outOfScope: boolean;
  /**
   * True when a failed row failed with a conflict (409/412) — the row's version was stale, not
   * that the write itself was rejected. `runSelectionAction`'s refresh gate widens on this: an
   * all-conflict batch still refreshes so an immediate retry has current versions to send
   * (F45.1, closes gitea-#201). Always `false` on a success outcome.
   */
  conflict: boolean;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function readNumberField(body: unknown, field: string): number {
  return isRecord(body) && typeof body[field] === "number" ? body[field] : 0;
}

/**
 * Runs `fn` over `items` with at most `concurrency` in flight at once, preserving `items` order
 * in the returned array. Selection mode fans a bulk action out into one request per selected row
 * (Finding 1, F28.11) — this bounds how many of those run concurrently.
 */
async function mapWithConcurrency<T, R>(
  items: T[],
  concurrency: number,
  fn: (item: T) => Promise<R>
): Promise<R[]> {
  const results: R[] = new Array(items.length);
  let cursor = 0;

  async function worker(): Promise<void> {
    while (cursor < items.length) {
      const index = cursor++;
      const item = items[index];
      if (item === undefined) continue;
      results[index] = await fn(item);
    }
  }

  await Promise.all(Array.from({ length: Math.min(concurrency, items.length) }, () => worker()));
  return results;
}

/** POST /api/media/{id}/reenrich?fields=<csv> for one row — the shipped single-row re-analyze
 * contract (matches ReanalyzePanel). No If-Match: re-enrichment doesn't touch tag/eligibility
 * columns under optimistic concurrency, but the status code is still classified for the shared
 * refresh gate (F45.1) in case the endpoint ever answers 409/412 for another reason. */
async function reenrichRow(item: AdminMediaDto, fieldsCsv: string): Promise<RowOutcome> {
  try {
    const resp = await fetch(`/api/media/${item.mediaId}/reenrich?fields=${fieldsCsv}`, { method: "POST" });
    return {
      mediaId: item.mediaId,
      ok: resp.ok,
      outOfScope: false,
      conflict: !resp.ok && (resp.status === 409 || resp.status === 412),
    };
  } catch {
    return { mediaId: item.mediaId, ok: false, outOfScope: false, conflict: false };
  }
}

/** POST /api/media/{id}/vote for one row (F33.3/F61.4) via the shared `voteTrack` fetcher, shaped
 * into `RowOutcome` for `runSelectionAction`. Rating writes are ETag-free by design (PLAN.md Epic
 * S sequencing note) — never a conflict, never out-of-scope (F61.3's scope-bounded rule is a
 * by-filter-only concern; the per-row `RatingController` keeps its F33.5 scope exemption). */
async function voteRow(item: AdminMediaDto, direction: VoteDirection): Promise<RowOutcome> {
  const outcome = await voteTrack(item.mediaId, direction);
  return { mediaId: item.mediaId, ok: outcome.ok, outOfScope: false, conflict: false };
}

/** PUT /api/media/{id}/never-play for one row (F33.4/F61.4) via the shared `setNeverPlay`
 * fetcher — same idempotent-set, ETag-free, scope-exempt posture as {@link voteRow}. Used for
 * both the flag ("Never play") and the clear ("Restore") toolbar actions. */
async function neverPlayRow(item: AdminMediaDto, neverPlay: boolean): Promise<RowOutcome> {
  const outcome = await setNeverPlay(item.mediaId, neverPlay);
  return { mediaId: item.mediaId, ok: outcome.ok, outOfScope: false, conflict: false };
}

/**
 * The contextual bulk-action toolbar (SPEC F28.11, STORY-089) — replaces the three
 * always-visible Bulk*Control components. Reassign / Re-enrich / Eligibility all confirm via
 * useConfirm (count-bearing copy), toast the outcome, and refresh the table on success. A
 * single `busy` flag (lifted to CatalogTable) blocks every button the moment any action starts,
 * so the toolbar can never open a second confirm while one is already pending (Q4 review:
 * useConfirm supports one pending confirm at a time).
 *
 * Selection actually scopes the write (Q7 review, Finding 1): with a non-empty selection, each
 * action fans out into one request per selected row via the shipped single-row endpoints
 * (PATCH /api/media/{id}, POST /api/media/{id}/reenrich) — never the bulk endpoints. With an
 * empty selection and an active filter (by-filter mode), the three bulk endpoints run exactly as
 * before, byte-compatible with the retired Bulk*Control components.
 */
export function CatalogToolbar({
  selectedMedia,
  totalMatchingRows,
  filter,
  libraries,
  busy,
  onBusyChange,
  onOutcome,
}: CatalogToolbarProps): ReactNode {
  const router = useRouter();
  const confirm = useConfirm();
  const selectionCount = selectedMedia.length;
  const selectionMode = selectionCount > 0;

  const [toLibraryId, setToLibraryId] = useState<number | null>(libraries[0]?.id ?? null);
  const [reenrichFields, setReenrichFields] = useState<Set<ReenrichField>>(new Set());

  // `notify: false` suppresses the shared hook's own per-row toast — the toolbar always shows one
  // summary toast across the whole selection instead (Q7 review). No `onConflict` is wired
  // either: `runSelectionAction`'s own `router.refresh()` reconciles every row's version once
  // anything in the batch changed OR any failure was a conflict (F45.1, widened from Q7's
  // `succeeded.length > 0` gate to also cover an all-conflict batch — closes gitea-#201); Q7's
  // failed-rows-stay-selected semantics are unchanged (SPEC F31.2–F31.3, F45.1, STORY-104).
  const { patchRow: sendRowPatch } = useRowPatch({ notify: false });

  /** PATCH /api/media/{id} for one row via the shared hook, shaped into the toolbar's
   * `RowOutcome` (mediaId + outOfScope for the reassign allow-and-warn case + conflict for the
   * refresh gate). */
  async function patchRow(item: AdminMediaDto, body: Record<string, unknown>): Promise<RowOutcome> {
    const outcome = await sendRowPatch({ mediaId: item.mediaId, version: item.version }, body);
    if (!outcome.ok) {
      return { mediaId: item.mediaId, ok: false, outOfScope: false, conflict: outcome.kind === "conflict" };
    }
    return {
      mediaId: item.mediaId,
      ok: true,
      outOfScope: isRecord(outcome.body) && outcome.body.outOfScope === true,
      conflict: false,
    };
  }

  function toggleReenrichField(field: ReenrichField): void {
    setReenrichFields((prev) => {
      const next = new Set(prev);
      if (next.has(field)) {
        next.delete(field);
      } else {
        next.add(field);
      }
      return next;
    });
  }

  /** Headline count for confirm copy: the operator's exact selection in selection mode (that IS
   * the blast radius now — no reminder needed), every matching row in filter mode. */
  function countPhrase(): string {
    return selectionMode
      ? `${selectionCount} selected track${selectionCount === 1 ? "" : "s"}`
      : `all ${totalMatchingRows} matching track${totalMatchingRows === 1 ? "" : "s"}`;
  }

  /** Runs a single request against the by-filter bulk endpoint (unchanged wire contract). */
  async function runAction(
    confirmOptions: { title: string; consequence: string },
    request: () => Promise<Response>,
    describeSuccess: (body: unknown) => string
  ): Promise<void> {
    onBusyChange(true);
    try {
      const ok = await confirm(confirmOptions);
      if (!ok) return;

      const resp = await request();
      if (resp.ok) {
        const body: unknown = await resp.json().catch(() => null);
        toast.success(describeSuccess(body));
        onOutcome(new Set());
        router.refresh();
        return;
      }

      toast.error(`Server error (${resp.status})`);
    } catch {
      toast.error("Network error — check your connection");
    } finally {
      onBusyChange(false);
    }
  }

  /**
   * Fans a selection-mode action out into one request per selected row (concurrency-bounded),
   * collects per-row outcomes, and reports a single summary toast: a success count when every
   * row succeeded, or an "N of M failed" error when any row failed. Succeeded rows leave the
   * selection; failed rows stay selected so the operator can see and retry them. Refreshes
   * afterward whenever something changed server-side OR any failure was a conflict — see the
   * refresh-gate comment below (F45.1).
   */
  async function runSelectionAction(
    confirmOptions: { title: string; consequence: string },
    perRow: (item: AdminMediaDto) => Promise<RowOutcome>,
    verb: string
  ): Promise<void> {
    onBusyChange(true);
    try {
      const ok = await confirm(confirmOptions);
      if (!ok) return;

      const outcomes = await mapWithConcurrency(selectedMedia, SELECTION_BATCH_CONCURRENCY, perRow);
      const failed = outcomes.filter((o) => !o.ok);
      const succeeded = outcomes.filter((o) => o.ok);
      const outOfScopeCount = succeeded.filter((o) => o.outOfScope).length;
      const outOfScopeNote =
        outOfScopeCount > 0 ? ` ${outOfScopeCount} left rotation (destination out of scope).` : "";

      if (failed.length > 0) {
        toast.error(`${failed.length} of ${outcomes.length} failed.${outOfScopeNote}`);
      } else {
        toast.success(`${succeeded.length} track${succeeded.length === 1 ? "" : "s"} ${verb}.${outOfScopeNote}`);
      }

      onOutcome(new Set(failed.map((o) => o.mediaId)));
      // Refresh when something actually changed server-side, OR when any failure was a conflict
      // (409/412) — an all-conflict batch means every row's local version is stale, and
      // refreshing pulls the current versions down so an immediate retry can succeed (F45.1,
      // closes gitea-#201, widened from the Q7 `succeeded.length > 0` gate). This must run AFTER
      // onOutcome narrows the selection down to the failed ids: CatalogTable's media-reconcile
      // effect keeps whatever's still selected once the refreshed `media` array lands, so
      // succeeded rows (already dropped from selection here) leave, and failed rows (still
      // selected here, and still present in the refreshed array) stay put. A total failure with
      // zero conflicts means nothing changed and no version is stale, so the refresh is still
      // skipped there (matches the by-filter error path, which also skips refresh on total
      // failure).
      if (succeeded.length > 0 || failed.some((o) => o.conflict)) router.refresh();
    } finally {
      onBusyChange(false);
    }
  }

  async function handleReassign(): Promise<void> {
    if (toLibraryId === null) return;
    const destination = libraries.find((l) => l.id === toLibraryId);
    if (destination === undefined) return;

    const confirmOptions = {
      title: "Reassign tracks",
      consequence: `Reassign ${countPhrase()} to "${destination.name}"?`,
    };

    if (selectionMode) {
      await runSelectionAction(
        confirmOptions,
        (item) => patchRow(item, { libraryId: toLibraryId }),
        "reassigned"
      );
      return;
    }

    await runAction(
      confirmOptions,
      () =>
        fetch("/api/media/bulk/reassign", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ filter, toLibraryId }),
        }),
      (body) => {
        const updated = readNumberField(body, "updated");
        return `${updated} track${updated === 1 ? "" : "s"} reassigned.`;
      }
    );
  }

  async function handleReenrich(): Promise<void> {
    const selectedFieldNames = ALL_REENRICH_FIELDS.filter((f) => reenrichFields.has(f));
    const fields = reenrichFields.size === 0 ? ALL_REENRICH_FIELDS.slice() : selectedFieldNames;
    const loudnessWarning =
      reenrichFields.has("loudness") || reenrichFields.size === 0
        ? " Rows queued for loudness re-measurement will leave rotation until measurement completes."
        : "";

    const confirmOptions = {
      title: "Re-analyze tracks",
      consequence: `Re-analyze ${countPhrase()} with fields: ${fields.join(", ")}?${loudnessWarning}`,
    };

    if (selectionMode) {
      // Empty selection == "all" here too — matches the shipped single-row ReanalyzePanel's csv
      // normalization exactly (fields query param: missing/empty means all four groups).
      const fieldsCsv = reenrichFields.size === 0 ? "all" : selectedFieldNames.join(",");
      await runSelectionAction(
        confirmOptions,
        (item) => reenrichRow(item, fieldsCsv),
        "scheduled for re-analysis"
      );
      return;
    }

    await runAction(
      confirmOptions,
      () =>
        fetch("/api/media/bulk/reenrich", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ filter, fields }),
        }),
      (body) => {
        const scheduled = readNumberField(body, "scheduled");
        return `${scheduled} track${scheduled === 1 ? "" : "s"} scheduled for re-analysis.`;
      }
    );
  }

  async function handleEligibility(eligible: boolean): Promise<void> {
    const confirmOptions = {
      title: eligible ? "Set tracks eligible" : "Set tracks ineligible",
      consequence: `Set ${countPhrase()} to ${eligible ? "eligible" : "ineligible"}?`,
    };

    if (selectionMode) {
      await runSelectionAction(
        confirmOptions,
        (item) => patchRow(item, { eligible }),
        eligible ? "set eligible" : "set ineligible"
      );
      return;
    }

    await runAction(
      confirmOptions,
      () =>
        fetch("/api/media/eligibility", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ eligible, filter }),
        }),
      (body) => {
        const affected = readNumberField(body, "affected");
        return `${affected} row${affected === 1 ? "" : "s"} updated.`;
      }
    );
  }

  /**
   * ±1 vote (F33.3/F61.1) on the whole selection or filter match. Selection mode fans out to the
   * shipped `POST /api/media/{id}/vote`; by-filter mode calls the new bulk endpoint (Z6,
   * `POST /api/media/bulk/vote`), gated by `hasBulkFilter` in CatalogTable exactly like the
   * shipped by-filter actions above.
   */
  async function handleVote(direction: VoteDirection): Promise<void> {
    const directionWord = direction === "up" ? "up" : "down";
    const confirmOptions = {
      title: direction === "up" ? "Vote tracks up" : "Vote tracks down",
      consequence: `Vote ${countPhrase()} ${directionWord}?`,
    };

    if (selectionMode) {
      await runSelectionAction(confirmOptions, (item) => voteRow(item, direction), `voted ${directionWord}`);
      return;
    }

    await runAction(
      confirmOptions,
      () =>
        fetch("/api/media/bulk/vote", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ filter, direction }),
        }),
      (body) => {
        const updated = readNumberField(body, "updated");
        return `${updated} track${updated === 1 ? "" : "s"} voted ${directionWord}.`;
      }
    );
  }

  /**
   * Sets or clears the never-play flag (F33.4/F61.1) across the selection or filter match — the
   * toolbar's "Never play" and "Restore" actions both call this, `neverPlay` telling them apart.
   * Restore is never gated behind a prior never-play filter (F61.2 — no one-way door): it renders
   * beside "Never play" in both modes, same as every other toolbar action.
   */
  async function handleNeverPlay(neverPlay: boolean): Promise<void> {
    const confirmOptions = {
      title: neverPlay ? "Flag tracks never-play" : "Restore tracks to rotation",
      consequence: neverPlay
        ? `Flag ${countPhrase()} as never-play?`
        : `Restore ${countPhrase()} to rotation?`,
    };

    if (selectionMode) {
      await runSelectionAction(
        confirmOptions,
        (item) => neverPlayRow(item, neverPlay),
        neverPlay ? "flagged never-play" : "restored"
      );
      return;
    }

    await runAction(
      confirmOptions,
      () =>
        fetch("/api/media/bulk/never-play", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ filter, neverPlay }),
        }),
      (body) => {
        const updated = readNumberField(body, "updated");
        return neverPlay
          ? `${updated} track${updated === 1 ? "" : "s"} flagged never-play.`
          : `${updated} track${updated === 1 ? "" : "s"} restored.`;
      }
    );
  }

  return (
    <section
      aria-label="Bulk actions"
      className="mb-3 flex flex-wrap items-center gap-4 rounded-[6px] border border-line bg-surface-2 px-4 py-3"
    >
      <p className="text-[0.85rem] font-semibold text-ink">
        {selectionMode ? `${selectionCount} selected` : `All ${totalMatchingRows} matching`}
      </p>

      {libraries.length > 0 && (
        <div className="flex items-center gap-1.5">
          <label htmlFor="bulk-reassign-library" className="sr-only">
            Destination library
          </label>
          <select
            id="bulk-reassign-library"
            aria-label="Destination library"
            value={toLibraryId ?? ""}
            onChange={(e) => {
              const val = parseInt(e.currentTarget.value, 10);
              setToLibraryId(isNaN(val) ? null : val);
            }}
            disabled={busy}
            className="h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.82rem] text-ink"
          >
            {libraries.map((lib) => (
              <option key={lib.id} value={lib.id}>
                {lib.name}
              </option>
            ))}
          </select>
          <Button
            type="button"
            variant="secondary"
            disabled={busy || toLibraryId === null}
            onClick={() => {
              void handleReassign();
            }}
          >
            Reassign
          </Button>
        </div>
      )}

      <fieldset className="flex items-center gap-2" disabled={busy}>
        <legend className="sr-only">Re-analyze fields</legend>
        {ALL_REENRICH_FIELDS.map((field) => (
          <label
            key={field}
            title={REENRICH_FIELD_HINTS[field]}
            className="flex min-h-10 items-center gap-1 text-[0.78rem] text-mute"
          >
            <input type="checkbox" checked={reenrichFields.has(field)} onChange={() => toggleReenrichField(field)} />
            {REENRICH_FIELD_LABELS[field]}
          </label>
        ))}
        <Button
          type="button"
          variant="secondary"
          disabled={busy}
          onClick={() => {
            void handleReenrich();
          }}
        >
          Re-analyze
        </Button>
      </fieldset>

      <div className="flex items-center gap-1.5">
        <Button
          type="button"
          variant="secondary"
          disabled={busy}
          onClick={() => {
            void handleEligibility(true);
          }}
        >
          Set eligible
        </Button>
        <Button
          type="button"
          variant="secondary"
          disabled={busy}
          onClick={() => {
            void handleEligibility(false);
          }}
        >
          Set ineligible
        </Button>
      </div>

      {/* Vote up/down + never-play/restore (SPEC F61.4) — icon-only, matching the row-level
          RatingControls/NeverPlayControl convention (Live page, Catalog rows): rendered through
          the shared `IconButton` so the hover/focus tooltip always carries the same copy as the
          aria-label (SPEC F62.1–F62.2). */}
      <div className="flex items-center gap-1.5">
        <IconButton
          label="Vote up"
          disabled={busy}
          onClick={() => {
            void handleVote("up");
          }}
        >
          <VoteUpIcon />
        </IconButton>
        <IconButton
          label="Vote down"
          disabled={busy}
          onClick={() => {
            void handleVote("down");
          }}
        >
          <VoteDownIcon />
        </IconButton>
        <IconButton
          label="Never play"
          disabled={busy}
          onClick={() => {
            void handleNeverPlay(true);
          }}
        >
          <CloseIcon />
        </IconButton>
        <IconButton
          label="Restore to rotation"
          disabled={busy}
          onClick={() => {
            void handleNeverPlay(false);
          }}
        >
          <RestoreIcon />
        </IconButton>
      </div>
    </section>
  );
}
