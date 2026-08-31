import type { ReactNode } from "react";
import { TabStrip, type TabStripTab } from "@/components/ui/tab-strip";
import {
  GARDENER_KIND_LABELS,
  GARDENER_KIND_ORDER,
  GARDENER_OPEN_COUNT_KEY,
  type GardenerKind,
  type GardenerOpenCounts,
} from "@/lib/gardener-api";
import { buildGardenerHref, type GardenerPageSize } from "./gardener-paging";

interface GardenerTabsProps {
  activeTab: GardenerKind;
  limit: GardenerPageSize;
  /** `GET /api/status`'s own per-kind OPEN totals (SPEC F153.9) — `null` when the status fetch
   * itself failed, in which case every tab renders unbadged rather than a wrong number: the page
   * fetches only the ACTIVE tab's own kind, so status is the only source for the other four. */
  open: GardenerOpenCounts | null;
}

function tabLabel(kind: GardenerKind, open: GardenerOpenCounts | null): string {
  const base = GARDENER_KIND_LABELS[kind];
  return open === null ? base : `${base} (${open[GARDENER_OPEN_COUNT_KEY[kind]]})`;
}

/**
 * The five rot-finding kind tabs (SPEC F153.10 rider 2026-08-31; STORY-381 AC1-AC3/AC7, gh-#654) —
 * URL-driven via `?tab=`, the shared `TabStrip` markup (gh-#393's extraction), each label badged
 * with that kind's own OPEN count from `/api/status` (STORY-381 AC1). `TabStrip` itself stays
 * untouched (T387 scope: the count is embedded IN the label string here rather than widening the
 * shared strip's own props) — every kind renders as its own tab regardless of count, the
 * `WardrobeTabs`/`PersonaCatalogTabs` "always render every kind" ruling applied here too.
 */
export function GardenerTabs({ activeTab, limit, open }: GardenerTabsProps): ReactNode {
  const tabs: TabStripTab<GardenerKind>[] = GARDENER_KIND_ORDER.map((kind) => ({
    id: kind,
    label: tabLabel(kind, open),
    href: buildGardenerHref(kind, limit),
  }));

  return <TabStrip tabs={tabs} activeTab={activeTab} ariaLabel="Gardener kinds" />;
}
