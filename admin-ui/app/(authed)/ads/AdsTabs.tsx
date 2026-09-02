import type { ReactNode } from "react";
import { TabStrip, type TabStripTab } from "@/components/ui/tab-strip";
import { AD_STATE_LABELS } from "@/lib/ads-api";
import { ADS_TAB_ORDER, buildAdsHref, type AdsPageSize, type AdsTabId } from "./ads-paging";

interface AdsTabsProps {
  activeTab: AdsTabId;
  limit: AdsPageSize;
}

const ADS_TAB_LABELS: Record<AdsTabId, string> = { ...AD_STATE_LABELS, briefs: "Briefs" };

/**
 * The seven Ads tabs (SPEC F162.1; STORY-392; PLAN T404) — the six spot-state tabs plus Briefs, on
 * the shared `TabStrip` (the Gardener kind-tab idiom this whole page follows).
 *
 * <b>Deliberately unbadged (T404's own judgment call — see the class remarks in `page.tsx`).</b>
 * The Gardener strip badges every tab from `GET /api/status`'s own per-kind OPEN counts — no
 * equivalent counts endpoint exists for ad spots (`/api/status` carries no `ads` block; SPEC F162.1
 * specs no such field). Badging every tab honestly would need one `GET /api/ads?state=X&limit=1`
 * round trip per OTHER tab (six calls, most of it thrown away) on every render of this page — the
 * dishonest alternative (a stale/estimated count, or badging only tabs that happen to already be
 * in hand) was rejected. The active tab's own EXACT total already renders in its section header and
 * the pager line below it; every other tab stays plainly labelled. If a real `/api/status` ads
 * block ever ships, this is the one place to wire it in (the Gardener `GardenerTabs` precedent for
 * how that would look).
 */
export function AdsTabs({ activeTab, limit }: AdsTabsProps): ReactNode {
  const tabs: TabStripTab<AdsTabId>[] = ADS_TAB_ORDER.map((tab) => ({
    id: tab,
    label: ADS_TAB_LABELS[tab],
    href: buildAdsHref(tab, limit),
  }));

  return <TabStrip tabs={tabs} activeTab={activeTab} ariaLabel="Ads sections" />;
}
