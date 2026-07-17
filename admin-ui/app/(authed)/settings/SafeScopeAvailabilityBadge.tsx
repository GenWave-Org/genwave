"use client";

import type { ReactNode } from "react";
import { usePoll } from "@/lib/use-poll";
import { fetchStatus } from "@/lib/broadcast-api";

interface SafeScopeAvailabilityBadgeProps {
  /**
   * True when the effective (bound) SafeScope value from GET /api/settings — the same
   * source the F25.4 "Silent on drain" badge reads — is an empty list. Config-level empty
   * scope takes precedence over the data-level depleted state below: the two badges are
   * mutually exclusive by construction, never both rendered.
   */
  effectivelyEmpty: boolean;
}

/**
 * SPEC F31.4–F31.5 (STORY-105, closes gitea-#186) — warns when the effective SafeScope has zero
 * playable tracks per the polled GET /api/status aggregate (`safeScope.playable`, the exact
 * /internal/safe-track predicate). Distinct copy and token treatment from the F25.4
 * empty-scope badge on purpose: an empty scope is a config choice operators made knowingly
 * (mksafe intentionally silences on drain, brass/quiet treatment); a depleted-but-non-empty
 * scope is an unnoticed data state — the tracks the operator picked all became unplayable —
 * so it gets the danger token.
 *
 * Mounted only for the SafeScope field (see SettingsForm), so it only polls when that field
 * is on the page. Polls at the shared 5 s cadence (lib/use-poll.ts) so a newly-depleted scope
 * surfaces within one poll without a reload. A poll failure leaves the badge as-is — absence
 * of data is not "depleted" — no toast, matching the dashboard tiles' quiet degrade.
 */
export function SafeScopeAvailabilityBadge({ effectivelyEmpty }: SafeScopeAvailabilityBadgeProps): ReactNode {
  const status = usePoll(fetchStatus);

  if (effectivelyEmpty) {
    return (
      <span className="inline-flex w-fit items-center gap-1.5 rounded-[3px] border border-accent-2 px-2 py-0.5 text-[0.72rem] font-semibold text-accent-2">
        Silent on drain — mksafe engaged
      </span>
    );
  }

  const depleted = status.data?.safeScope?.playable === 0;
  if (!depleted) return null;

  return (
    <span className="inline-flex w-fit items-center gap-1.5 rounded-[3px] border border-danger px-2 py-0.5 text-[0.72rem] font-semibold text-danger">
      Safe scope has no playable tracks — drains will be silent
    </span>
  );
}
