"use client";

import { useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { AD_STATE_EMPTY_LABELS, AD_STATE_LABELS, type AdSpotDto, type AdState } from "@/lib/ads-api";
import type { SponsorRefDto } from "@/lib/sponsors-api";
import { AdSpotEditor } from "./AdSpotEditor";
import { AdSpotRow } from "./AdSpotRow";
import { SpotWizard } from "./SpotWizard";

interface AdsSectionProps {
  tab: AdState;
  items: AdSpotDto[];
  /** The tab's own EXACT total (SPEC F162.1's "the active tab's total in the pager line" — see
   * `AdsTabs`' own remarks for why every OTHER tab stays unbadged instead). */
  total: number;
  /** The rail's current selection (PLAN T447) — preselected in a freshly-opened "New spot" wizard
   * or editor; `null` means "All sponsors" and nothing starts chosen. */
  sponsorId: number | null;
  /** Every sponsor the wizard's/editor's own picker offers (PLAN T447) — id/name/paused only. */
  sponsors: readonly SponsorRefDto[];
}

/** `null` = nothing open, `"new"` = the SpotWizard (create), an `AdSpotDto` = `AdSpotEditor`
 * editing that row. */
type EditorTarget = "new" | AdSpotDto | null;

/**
 * One state tab's own content pane (SPEC F162.1, F174.1; STORY-392, STORY-421; PLAN T404, T448) —
 * mirrors `gardener/GardenerSection.tsx`'s own split exactly: `page.tsx` (a Server Component)
 * renders this directly as the page's "use client" boundary, owning `useRouter()` and threading a
 * `router.refresh()` closure down to every row verb AND `AdSpotEditor`'s own save — never a
 * client-held local patch of the list (SPEC F162.1's own re-fetch-fresh posture, the same law
 * Gardener already holds). `SpotWizard` owns its own `router.refresh()` on approve (PLAN T448) —
 * it never runs through this section's `onChanged`, since it closes on a different event (approve)
 * than `AdSpotEditor`'s single save.
 *
 * "New spot…" renders on every state tab, not just Draft — a new spot is always born a draft
 * regardless of which tab is currently active (mirrors an ordinary "New" affordance staying
 * reachable from any filtered view); it simply won't appear in the CURRENT list until the operator
 * switches to the Draft tab (or a `router.refresh()` on that tab picks it up). It opens the
 * `SpotWizard` (PLAN T448 — `AdSpotEditor`'s own free-text create path is gone); `AdSpotEditor`
 * now only ever renders for an EDIT target (`editing !== "new"`).
 */
export function AdsSection({ tab, items, total, sponsorId, sponsors }: AdsSectionProps): ReactNode {
  const router = useRouter();
  const onChanged = (): void => router.refresh();

  const [editing, setEditing] = useState<EditorTarget>(null);

  const label = AD_STATE_LABELS[tab];

  return (
    <section aria-label={label} className="rounded-[6px] border border-line bg-surface p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-display text-[1.05rem] font-semibold text-ink">
          {label}
          <span className="text-[0.85rem] font-normal text-mute"> · {total} total</span>
        </h2>
        <Button type="button" onClick={() => setEditing("new")}>
          New spot…
        </Button>
      </div>

      {items.length === 0 && <p className="mt-3 text-[0.85rem] text-mute">{AD_STATE_EMPTY_LABELS[tab]}</p>}

      {items.length > 0 && (
        <div className="mt-3 divide-y divide-line">
          {items.map((spot) => (
            <AdSpotRow key={spot.id} spot={spot} onChanged={onChanged} onEdit={() => setEditing(spot)} />
          ))}
        </div>
      )}

      {editing === "new" && (
        <SpotWizard sponsors={sponsors} initialSponsorId={sponsorId} onClose={() => setEditing(null)} />
      )}

      {editing !== null && editing !== "new" && (
        <AdSpotEditor
          initial={editing}
          sponsors={sponsors}
          onCancel={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            onChanged();
          }}
        />
      )}
    </section>
  );
}
