"use client";

import { useState, type ReactNode } from "react";
import {
  fetchAnnouncementHistory,
  fetchAnnounceTokenStatus,
  type AnnounceTokenStatusDto,
  type AnnouncementHistoryDto,
} from "@/lib/announcements-api";
import { AnnounceTokenPanel } from "./AnnounceTokenPanel";
import { AnnouncementComposer } from "./AnnouncementComposer";
import { AnnouncementHistoryList } from "./AnnouncementHistoryList";

export interface AnnouncementsClientProps {
  initialHistory: AnnouncementHistoryDto[];
  initialTokenStatus: AnnounceTokenStatusDto;
  /** Test-only injection point threaded down to the history list / token panel's own timestamp
   * formatters; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

/**
 * Client half of the Announcements page (SPEC F146, STORY-361, PLAN T344) — composes the send form
 * (F146.1, AC1), the history list (F146.2, AC2 — the F143.2 visible-decline surface), and the token
 * section (F146.3, AC3). Owns the two pieces of state a mutation on one child can invalidate: the
 * history list (a successful send needs to appear) and the token status (generate/regenerate/revoke
 * each change it) — both children stay presentational, reporting outcomes upward rather than
 * fetching their own copies, the same split `SafeContentClient` keeps from its own children.
 */
export function AnnouncementsClient({
  initialHistory,
  initialTokenStatus,
  timeZone,
}: AnnouncementsClientProps): ReactNode {
  const [history, setHistory] = useState<AnnouncementHistoryDto[]>(initialHistory);
  const [tokenStatus, setTokenStatus] = useState<AnnounceTokenStatusDto>(initialTokenStatus);

  async function refreshHistory(): Promise<void> {
    const rows = await fetchAnnouncementHistory();
    if (rows !== null) setHistory(rows);
  }

  async function refreshTokenStatus(): Promise<void> {
    const status = await fetchAnnounceTokenStatus();
    if (status !== null) setTokenStatus(status);
  }

  return (
    <div className="flex flex-col gap-6">
      <AnnouncementComposer onSent={() => { void refreshHistory(); }} />

      <section aria-label="Announcement history">
        <p className="text-[0.7rem] font-semibold uppercase tracking-[0.14em] text-accent-2">
          History
        </p>
        <AnnouncementHistoryList entries={history} timeZone={timeZone} />
      </section>

      <AnnounceTokenPanel
        status={tokenStatus}
        onStatusChanged={() => { void refreshTokenStatus(); }}
        timeZone={timeZone}
      />
    </div>
  );
}
