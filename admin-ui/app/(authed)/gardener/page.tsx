import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { Pager } from "@/components/ui/pager";
import {
  buildGardenerFindingsPath,
  GARDENER_OPEN_COUNT_KEY,
  type GardenerFindingsResponse,
  type GardenerGroupDto,
  type GardenerKind,
  type GardenerOpenCounts,
} from "@/lib/gardener-api";
import { GardenerTabs } from "./GardenerTabs";
import { GardenerSection } from "./GardenerSection";
import { GardenerPageSizePicker } from "./GardenerPageSizePicker";
import {
  buildGardenerPageHref,
  resolveGardenerPageCount,
  resolveGardenerPaging,
  type GardenerSearchParams,
} from "./gardener-paging";

// The Library Gardener's own admin page (SPEC F153.10 rider 2026-08-31; STORY-381/382/383; PLAN
// T387, gh-#654/#655/#657): a server-rendered route reading `?tab=&page=&limit=` — one tab strip
// (GardenerTabs), one kind's own section (GardenerSection), and a plain-anchor pager/size picker,
// the catalog page's own idiom (`catalog/page.tsx`) applied here. Auth is already enforced by
// middleware.ts on this route. The queue changes via every row verb (dismiss/eligibility/
// never-play/re-enrich/Keep this one/purge) — always re-render fresh, mirroring catalog/page.tsx's
// own posture, rather than the retired GardenerView's client-side polling-free-but-still-client
// fetch (gh-#654).
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

interface GardenerPageProps {
  searchParams: Promise<GardenerSearchParams>;
}

const EMPTY_GROUP = (kind: GardenerKind): GardenerGroupDto => ({ kind, findings: [], duplicateGroups: [] });

/** `GET /api/status`'s own `gardener.open` block — the tab strip's badge source (SPEC F153.9).
 * Narrow, unvalidated read of a 2xx body (mirrors `personas/page.tsx`'s own `StatusRow`): a shape
 * surprise degrades to "no badges" rather than throwing mid-render. */
interface StatusRow {
  gardener?: { open: GardenerOpenCounts };
}

const PAGE_TITLE = <h1 className="font-display text-[1.35rem] font-semibold text-ink">Gardener</h1>;

export default async function GardenerPage({ searchParams }: GardenerPageProps): Promise<ReactNode> {
  const sp = await searchParams;
  const { tab, page, limit, offset } = resolveGardenerPaging(sp);
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  // Status is a best-effort degrade (the tab badges only, mirrors personas/page.tsx's own
  // on-air-badge posture) — a reject or non-2xx here must never take the required findings read
  // down with it, so this rides Promise.allSettled rather than Promise.all.
  const [statusResult, findingsResult] = await Promise.allSettled([
    apiGet("/api/status", { cookies: cookieHeader }),
    apiGet(buildGardenerFindingsPath(tab, limit, offset), { cookies: cookieHeader }),
  ]);

  const open: GardenerOpenCounts | null =
    statusResult.status === "fulfilled" && statusResult.value.ok
      ? ((await statusResult.value.json()) as StatusRow).gardener?.open ?? null
      : null;

  if (findingsResult.status === "rejected" || !findingsResult.value.ok) {
    return (
      <main>
        {PAGE_TITLE}
        <div className="mt-4">
          <GardenerTabs activeTab={tab} limit={limit} open={open} />
        </div>
        <p className="mt-6 text-[0.85rem] text-danger">Unable to load the Gardener queue.</p>
      </main>
    );
  }

  const body = (await findingsResult.value.json()) as GardenerFindingsResponse;
  const group = body.groups.find((candidate) => candidate.kind === tab) ?? EMPTY_GROUP(tab);
  // `total` is a JSON NUMBER on a kind-scoped response (T386's own guaranteed shape, this call is
  // always kind-scoped) — the fallback below only guards an off-shape/malformed body, mirroring
  // this page's other unvalidated-2xx-body reads.
  const total = typeof body.total === "number" ? body.total : group.findings.length;
  const pages = resolveGardenerPageCount(total, limit);

  return (
    <main>
      {PAGE_TITLE}

      <div className="mt-4">
        <GardenerTabs activeTab={tab} limit={limit} open={open} />
      </div>

      <div className="mt-6">
        <GardenerSection
          kind={tab}
          group={group}
          total={total}
          openCount={open !== null ? open[GARDENER_OPEN_COUNT_KEY[tab]] : null}
        />
      </div>

      <Pager page={page} pages={pages} hrefFor={(target) => buildGardenerPageHref(tab, limit, target)} />
      <GardenerPageSizePicker kind={tab} limit={limit} />
    </main>
  );
}
