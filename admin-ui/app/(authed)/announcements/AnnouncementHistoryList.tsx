"use client";

import type { ReactNode } from "react";
import { EmptyState } from "@/components/ui/empty-state";
import { formatUpSince } from "@/lib/format-clock";
import { cn } from "@/lib/utils";
import type { AnnouncementHistoryDto, AnnouncementState } from "@/lib/announcements-api";

type ChipVariant = "ok" | "warning" | "muted";

/**
 * Maps the SPEC F143.2 total state machine onto the house state-chip vocabulary (mirrors
 * `HealthView.stateChip`'s own shape one page over): `aired` is the happy ending (success/olive),
 * `declined` is the one state worth flagging (danger/rust-red — this IS the visible-decline surface,
 * F146.2), everything else (`pending`/`claimed`/`expired`) is a quiet, unremarkable in-progress or
 * natural-timeout state. Exported for the jsdom suite to assert against directly.
 */
export function announcementStateChip(state: AnnouncementState): { label: string; variant: ChipVariant } {
  switch (state) {
    case "aired":
      return { label: "aired", variant: "ok" };
    case "declined":
      return { label: "declined", variant: "warning" };
    case "pending":
    case "claimed":
    case "expired":
      return { label: state, variant: "muted" };
  }
}

const CHIP_CLASSES: Record<ChipVariant, string> = {
  ok: "border-success text-success",
  warning: "border-danger text-danger",
  muted: "border-line text-mute",
};

function AnnouncementStateChip({ state }: { state: AnnouncementState }): ReactNode {
  const chip = announcementStateChip(state);
  return (
    <span
      className={cn(
        "inline-flex w-fit items-center rounded-[999px] border px-2 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.12em]",
        CHIP_CLASSES[chip.variant]
      )}
    >
      {chip.label}
    </span>
  );
}

export interface AnnouncementHistoryListProps {
  entries: AnnouncementHistoryDto[];
  /** Test-only injection point for the timestamp formatter; production omits this and gets the
   * browser's local zone (mirrors BoothLogFeed's own `timeZone` prop). */
  timeZone?: string;
}

/**
 * The Announcements page's own history list (SPEC F146.2, STORY-361, PLAN T344) — THE F143.2
 * visible-decline/visible-expiry surface: every row this station's store has ever transitioned,
 * newest first, with its state, decline reason where present, collapse count, and aired timestamp.
 * Table shape mirrors `BoothLogFeed`'s own narrative-feed convention (a house precedent for
 * "list of events" content, not just tabular numbers).
 */
export function AnnouncementHistoryList({ entries, timeZone }: AnnouncementHistoryListProps): ReactNode {
  if (entries.length === 0) {
    return (
      <EmptyState
        className="mt-3"
        title="No announcements yet"
        reason="Send one above — it appears here the moment it's accepted, whatever happens to it next."
      />
    );
  }

  return (
    <div className="mt-3 overflow-x-auto">
      <table className="w-full border-collapse text-[0.85rem]">
        <thead>
          <tr className="border-b-2 border-line text-left">
            <th scope="col" className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
              Message
            </th>
            <th scope="col" className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
              State
            </th>
            <th scope="col" className="py-2 pr-3 text-right text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
              Collapsed
            </th>
            <th scope="col" className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
              Sent
            </th>
            <th scope="col" className="py-2 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
              Aired
            </th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => (
            <tr key={entry.id} className="border-b border-line last:border-b-0">
              <td className="py-2 pr-3 text-ink">
                {entry.message}
                {entry.verbatim && (
                  <span className="ml-1.5 text-[0.7rem] text-mute">(verbatim)</span>
                )}
                {entry.state === "declined" && entry.declineReason !== null && (
                  <p className="mt-1 text-[0.75rem] text-danger">{entry.declineReason}</p>
                )}
              </td>
              <td className="py-2 pr-3">
                <AnnouncementStateChip state={entry.state} />
              </td>
              <td className="py-2 pr-3 text-right tabular-nums text-mute">
                {entry.collapseCount > 1 ? `×${entry.collapseCount}` : ""}
              </td>
              <td className="py-2 pr-3 whitespace-nowrap tabular-nums text-mute">
                {formatUpSince(entry.createdAt, { timeZone })}
              </td>
              <td className="py-2 whitespace-nowrap tabular-nums text-mute">
                {entry.airedAt !== null ? formatUpSince(entry.airedAt, { timeZone }) : "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
