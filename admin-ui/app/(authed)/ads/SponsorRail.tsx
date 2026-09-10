import Link from "next/link";
import type { ReactNode } from "react";
import { Chip } from "@/components/ui/chip";
import { cn } from "@/lib/utils";
import type { SponsorListItemDto } from "@/lib/sponsors-api";
import { buildSponsorHref, type AdsPageSize, type AdsTabId } from "./ads-paging";

interface SponsorRailProps {
  sponsors: readonly SponsorListItemDto[];
  /** The rail's current selection — `null` means "All sponsors". */
  sponsorId: number | null;
  tab: AdsTabId;
  limit: AdsPageSize;
}

/** Every ad-spot state's own count summed into one number (SPEC F171.8's "spots-total count") —
 * `SponsorListItemDto.spots` carries a zero-absent map (a state with no spots is simply missing
 * the key, never an explicit `0`), so a plain sum over whatever values ARE present is already
 * correct with no zero-fill step first. */
function spotsTotal(spots: Record<string, number>): number {
  return Object.values(spots).reduce((sum, count) => sum + count, 0);
}

const ROW_CLASSES =
  "flex flex-col gap-1 rounded-[6px] border border-transparent px-3 py-2 text-left transition-colors duration-[120ms] ease-out";
const ROW_ACTIVE_CLASSES = "border-line bg-surface";
const ROW_INACTIVE_CLASSES = "hover:bg-surface/60";

/**
 * The Ads page's sponsor rail (SPEC F171.8; STORY-413; PLAN T447) — one plain-anchor row per
 * sponsor (name, briefs count, spots-total count, a "Paused" badge for a paused one) plus an "All
 * sponsors" row on top, on the same href-driven selection grammar `TabStrip`/`Pager`/
 * `PageSizePicker` already hold for this page: no client state, the URL's own `?sponsor=` decides
 * what is selected (`ads-paging.ts`'s {@link buildSponsorHref}), so the rail works from a Server
 * Component and survives a refresh or a shared link exactly like the tab strip beside it.
 *
 * Zero sponsors is a real, renderable state (a fresh station with none installed yet) — not an
 * error — so this renders one plain sentence instead of an empty nav landmark with nothing in it.
 */
export function SponsorRail({ sponsors, sponsorId, tab, limit }: SponsorRailProps): ReactNode {
  if (sponsors.length === 0) {
    return (
      <aside aria-label="Sponsors" className="w-56 shrink-0">
        <p className="text-[0.85rem] text-mute">No sponsors yet.</p>
      </aside>
    );
  }

  return (
    <nav aria-label="Sponsors" className="flex w-56 shrink-0 flex-col gap-1">
      <Link
        href={buildSponsorHref(tab, limit, null)}
        aria-current={sponsorId === null ? "true" : undefined}
        className={cn(ROW_CLASSES, sponsorId === null ? ROW_ACTIVE_CLASSES : ROW_INACTIVE_CLASSES)}
      >
        <span className="text-[0.85rem] font-semibold text-ink">All sponsors</span>
      </Link>

      {sponsors.map((sponsor) => (
        <Link
          key={sponsor.id}
          href={buildSponsorHref(tab, limit, sponsor.id)}
          aria-current={sponsorId === sponsor.id ? "true" : undefined}
          className={cn(ROW_CLASSES, sponsorId === sponsor.id ? ROW_ACTIVE_CLASSES : ROW_INACTIVE_CLASSES)}
        >
          <span className="flex items-center justify-between gap-2">
            <span className="truncate text-[0.85rem] font-semibold text-ink">{sponsor.name}</span>
            {sponsor.paused && <Chip>Paused</Chip>}
          </span>
          <span className="text-[0.75rem] text-mute">
            {sponsor.briefs} briefs · {spotsTotal(sponsor.spots)} spots
          </span>
        </Link>
      ))}
    </nav>
  );
}
