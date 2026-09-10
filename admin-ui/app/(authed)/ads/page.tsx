import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { Pager } from "@/components/ui/pager";
import { PageSizePicker } from "@/components/ui/page-size-picker";
import { AD_BRIEFS_PATH, buildAdsListPath, type AdBriefDto, type AdsListResponse } from "@/lib/ads-api";
import { SPONSORS_PATH, type SponsorListItemDto, type SponsorRefDto } from "@/lib/sponsors-api";
import { AdsTabs } from "./AdsTabs";
import { AdsSection } from "./AdsSection";
import { BriefsSection } from "./BriefsSection";
import { SponsorRail } from "./SponsorRail";
import {
  ADS_PAGE_SIZES,
  buildAdsHref,
  buildAdsPageHref,
  resolveAdsPageCount,
  resolveAdsPaging,
  resolveSponsorId,
  type AdsSearchParams,
} from "./ads-paging";

// The Ads admin page (SPEC F162.1, F171.8; STORY-392, STORY-413; PLAN T404, T447) — a
// server-rendered route reading `?tab=&page=&limit=&sponsor=`, following the Gardener page's own
// layout grammar (`gardener/page.tsx`, SPEC F153.10 rider, PLAN T387) verbatim: a left sponsor
// rail (`SponsorRail`, PLAN T447), then one tab strip (AdsTabs) and one tab's own content pane
// (AdsSection for a spot-state tab, BriefsSection for the Briefs tab) scoped to the rail's
// selection, a plain-anchor pager/size picker for the paged tabs. Auth is already enforced by
// middleware.ts on this route. The list changes via every row verb (approve/retry/retire/edit) and
// every briefs-tab mutation (add/toggle) — always re-render fresh (router.refresh(), threaded
// through AdsSection/BriefsSection), never a client-side patch, mirroring the Gardener page's own
// posture.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

interface AdsPageProps {
  searchParams: Promise<AdsSearchParams>;
}

const PAGE_TITLE = <h1 className="font-display text-[1.35rem] font-semibold text-ink">Ads</h1>;

/** A rejected fetch (network error, DNS, ...) must never throw out of this Server Component and
 * 500 the whole page — there's no error.tsx here — mirrors `gardener/page.tsx`'s own
 * `Promise.allSettled` posture. */
async function fetchAdsData(path: string, cookieHeader: string): Promise<Response | null> {
  try {
    return await apiGet(path, { cookies: cookieHeader });
  } catch {
    return null;
  }
}

/** `SponsorRefDto` (id/name/paused) is all every sponsor `<select>` on this page needs — never the
 * full `SponsorListItemDto` the rail itself renders. */
function toSponsorRefs(sponsors: readonly SponsorListItemDto[]): SponsorRefDto[] {
  return sponsors.map((sponsor) => ({ id: sponsor.id, name: sponsor.name, paused: sponsor.paused }));
}

export default async function AdsPage({ searchParams }: AdsPageProps): Promise<ReactNode> {
  const sp = await searchParams;
  const { tab, page, limit, offset } = resolveAdsPaging(sp);
  const rawSponsorId = resolveSponsorId(sp.sponsor);
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  const dataPath = tab === "briefs" ? AD_BRIEFS_PATH : buildAdsListPath(tab, limit, offset, rawSponsorId);
  // PLAN T447 ruling: the rail needs the sponsor list on every render of this page, not just when
  // a sponsor is selected — both fetches run in parallel, keyed on the raw (unvalidated) sponsor
  // id, so the common case (no selection, or a selection that matches a real sponsor) costs
  // exactly one round trip; either fetch failing collapses to the page's one existing failure
  // message (no partial two-column render).
  const [sponsorsResponse, dataResponse] = await Promise.all([
    fetchAdsData(SPONSORS_PATH, cookieHeader),
    fetchAdsData(dataPath, cookieHeader),
  ]);

  const failureMessage = tab === "briefs" ? "Unable to load briefs." : "Unable to load the ads library.";
  if (sponsorsResponse === null || !sponsorsResponse.ok || dataResponse === null || !dataResponse.ok) {
    return (
      <main>
        {PAGE_TITLE}
        <div className="mt-4">
          <AdsTabs activeTab={tab} limit={limit} sponsorId={rawSponsorId} />
        </div>
        <p className="mt-6 text-[0.85rem] text-danger">{failureMessage}</p>
      </main>
    );
  }

  const sponsors = (await sponsorsResponse.json()) as SponsorListItemDto[];
  // PLAN T447 ruling: an id that matches no fetched sponsor (deleted, or a typo) means "All
  // sponsors" — never a rail with no row marked current and a pane scoped to an id nothing else
  // on the page honors.
  const sponsorId =
    rawSponsorId !== null && sponsors.some((sponsor) => sponsor.id === rawSponsorId) ? rawSponsorId : null;
  const sponsorRefs = toSponsorRefs(sponsors);
  const rail = <SponsorRail sponsors={sponsors} sponsorId={sponsorId} tab={tab} limit={limit} />;
  const tabStrip = (
    <div className="mt-4">
      <AdsTabs activeTab={tab} limit={limit} sponsorId={sponsorId} />
    </div>
  );

  if (tab === "briefs") {
    // No `sponsorId` filter exists on `GET /api/ad-briefs` (PLAN T447's own reading of the API) —
    // the rail's selection scopes this tab client-side, by `brief.sponsor.id`; the validated
    // `sponsorId` (not `rawSponsorId`) drives the filter, so a stale bookmark reads as "All
    // sponsors" here too.
    const allBriefs = (await dataResponse.json()) as AdBriefDto[];
    const briefs = sponsorId === null ? allBriefs : allBriefs.filter((brief) => brief.sponsor.id === sponsorId);

    return (
      <main>
        {PAGE_TITLE}
        {tabStrip}
        <div className="mt-6 flex gap-6">
          {rail}
          <div className="min-w-0 flex-1">
            <BriefsSection key={sponsorId ?? "all"} briefs={briefs} sponsorId={sponsorId} sponsors={sponsorRefs} />
          </div>
        </div>
      </main>
    );
  }

  // PLAN T447 ruling: the spots fetch above ran keyed on `rawSponsorId`; only when validation
  // actually changed the id (a stale bookmark) does the list need a second, `sponsorId`-keyed
  // fetch — a valid selection (or none) never pays for it.
  const spotsResponse =
    sponsorId === rawSponsorId
      ? dataResponse
      : await fetchAdsData(buildAdsListPath(tab, limit, offset, sponsorId), cookieHeader);
  if (spotsResponse === null || !spotsResponse.ok) {
    return (
      <main>
        {PAGE_TITLE}
        {tabStrip}
        <p className="mt-6 text-[0.85rem] text-danger">{failureMessage}</p>
      </main>
    );
  }

  const body = (await spotsResponse.json()) as AdsListResponse;
  const pages = resolveAdsPageCount(body.total, limit);

  return (
    <main>
      {PAGE_TITLE}
      {tabStrip}
      <div className="mt-6 flex gap-6">
        {rail}
        <div className="min-w-0 flex-1">
          <AdsSection tab={tab} items={body.items} total={body.total} sponsorId={sponsorId} sponsors={sponsorRefs} />
          <Pager page={page} pages={pages} hrefFor={(target) => buildAdsPageHref(tab, limit, sponsorId, target)} />
          <PageSizePicker
            sizes={ADS_PAGE_SIZES}
            limit={limit}
            hrefFor={(size) => buildAdsHref(tab, size, sponsorId)}
          />
        </div>
      </div>
    </main>
  );
}
