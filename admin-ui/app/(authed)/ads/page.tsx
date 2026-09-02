import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { Pager } from "@/components/ui/pager";
import { PageSizePicker } from "@/components/ui/page-size-picker";
import { AD_BRIEFS_PATH, buildAdsListPath, type AdBriefDto, type AdsListResponse } from "@/lib/ads-api";
import { AdsTabs } from "./AdsTabs";
import { AdsSection } from "./AdsSection";
import { BriefsSection } from "./BriefsSection";
import {
  ADS_PAGE_SIZES,
  buildAdsHref,
  buildAdsPageHref,
  resolveAdsPageCount,
  resolveAdsPaging,
  type AdsSearchParams,
} from "./ads-paging";

// The Ads admin page (SPEC F162.1; STORY-392; PLAN T404) — a server-rendered route reading
// `?tab=&page=&limit=`, following the Gardener page's own layout grammar (`gardener/page.tsx`,
// SPEC F153.10 rider, PLAN T387) verbatim: one tab strip (AdsTabs), one tab's own content pane
// (AdsSection for a spot-state tab, BriefsSection for the Briefs tab), a plain-anchor pager/size
// picker for the paged tabs. Auth is already enforced by middleware.ts on this route. The list
// changes via every row verb (approve/retry/retire/edit) and every briefs-tab mutation
// (add/toggle) — always re-render fresh (router.refresh(), threaded through AdsSection/
// BriefsSection), never a client-side patch, mirroring the Gardener page's own posture.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

interface AdsPageProps {
  searchParams: Promise<AdsSearchParams>;
}

const PAGE_TITLE = <h1 className="font-display text-[1.35rem] font-semibold text-ink">Ads</h1>;

/** A rejected fetch (network error, DNS, ...) must never throw out of this Server Component and
 * 500 the whole page — there's no error.tsx here — mirrors `gardener/page.tsx`'s own
 * `Promise.allSettled` posture, collapsed to a single try/catch since this page issues exactly one
 * required GET per render (state list OR the bare briefs list, never both). */
async function fetchAdsData(path: string, cookieHeader: string): Promise<Response | null> {
  try {
    return await apiGet(path, { cookies: cookieHeader });
  } catch {
    return null;
  }
}

export default async function AdsPage({ searchParams }: AdsPageProps): Promise<ReactNode> {
  const sp = await searchParams;
  const { tab, page, limit, offset } = resolveAdsPaging(sp);
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  const tabStrip = (
    <div className="mt-4">
      <AdsTabs activeTab={tab} limit={limit} />
    </div>
  );

  if (tab === "briefs") {
    const response = await fetchAdsData(AD_BRIEFS_PATH, cookieHeader);
    if (response === null || !response.ok) {
      return (
        <main>
          {PAGE_TITLE}
          {tabStrip}
          <p className="mt-6 text-[0.85rem] text-danger">Unable to load briefs.</p>
        </main>
      );
    }

    const briefs = (await response.json()) as AdBriefDto[];
    return (
      <main>
        {PAGE_TITLE}
        {tabStrip}
        <div className="mt-6">
          <BriefsSection briefs={briefs} />
        </div>
      </main>
    );
  }

  const response = await fetchAdsData(buildAdsListPath(tab, limit, offset), cookieHeader);
  if (response === null || !response.ok) {
    return (
      <main>
        {PAGE_TITLE}
        {tabStrip}
        <p className="mt-6 text-[0.85rem] text-danger">Unable to load the ads library.</p>
      </main>
    );
  }

  const body = (await response.json()) as AdsListResponse;
  const pages = resolveAdsPageCount(body.total, limit);

  return (
    <main>
      {PAGE_TITLE}
      {tabStrip}
      <div className="mt-6">
        <AdsSection tab={tab} items={body.items} total={body.total} />
      </div>
      <Pager page={page} pages={pages} hrefFor={(target) => buildAdsPageHref(tab, limit, target)} />
      <PageSizePicker sizes={ADS_PAGE_SIZES} limit={limit} hrefFor={(size) => buildAdsHref(tab, size)} />
    </main>
  );
}
