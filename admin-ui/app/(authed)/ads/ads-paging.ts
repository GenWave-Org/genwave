// The Ads page's own `?tab=&page=&limit=` resolution and href builders (SPEC F162.1; STORY-392;
// PLAN T404) — mirrors `gardener/gardener-paging.ts` (the T387 precedent this page's whole layout
// grammar follows). Plain TypeScript, no JSX and no Next.js request APIs, kept separate from
// `page.tsx` for the same reason: the one place this page's searchParams get parsed and
// re-assembled, testable by calling a function.
//
// URL semantics (binding, mirrors the Gardener rider's own three rules):
//  - tab: one of AD_STATE_TOKENS, or the literal "briefs" — absent, repeated, or unrecognised all
//    fall back to the FIRST state tab, silently — never a 400.
//  - page: a positive integer; absent/non-numeric/less than 1 falls back to 1. Clamped against
//    Int32 overflow (the Gardener MED-1 fix, applied here identically) — the Ads API's own
//    `offset` query parameter is the same C# `int?` shape.
//  - limit: one of ADS_PAGE_SIZES; absent or out-of-set falls back to DEFAULT_ADS_PAGE_SIZE, which
//    equals `AdsController.DefaultLimit` (50) — the picker's own default reads the same number the
//    api would apply anyway if this page sent no `limit=` at all.
//  - The "briefs" tab is deliberately unpaged (T403b's own bare, unpaged `GET /api/ad-briefs`) —
//    `page`/`limit` are still parsed (so a stray `?page=` on that tab never throws), but `page.tsx`
//    never builds a Pager/size-picker href for it.

import { AD_STATE_TOKENS, type AdState } from "@/lib/ads-api";

/** Every tab this page's strip renders: the six spot-state tabs plus the Briefs tab (SPEC F162.1's
 * own "a Briefs tab" — one strip, not a second one). */
export type AdsTabId = AdState | "briefs";

export const ADS_TAB_ORDER: readonly AdsTabId[] = [
  ...AD_STATE_TOKENS,
  "briefs",
] as const satisfies readonly AdsTabId[];

export const ADS_PAGE_SIZES = [25, 50, 100, 200] as const;
export type AdsPageSize = (typeof ADS_PAGE_SIZES)[number];
/** Mirrors `AdsController.DefaultLimit` — the page's own default reads the same number the api
 * would apply if this page sent no `limit=` param at all. */
export const DEFAULT_ADS_PAGE_SIZE: AdsPageSize = 50;

export interface AdsSearchParams {
  tab?: string | string[];
  page?: string | string[];
  limit?: string | string[];
}

export interface ResolvedAdsPaging {
  tab: AdsTabId;
  page: number;
  limit: AdsPageSize;
  offset: number;
}

/** The URL's own founding tab — bare `/ads` and every tab-preserving href omit `?tab=` entirely
 * for this one, mirroring `gardener-paging.ts`'s own `FIRST_GARDENER_TAB`. `AD_STATE_TOKENS`'s own
 * `as const satisfies` typing keeps `[0]` a plain `AdState`, never `AdState | undefined`, under
 * `noUncheckedIndexedAccess`. */
const FIRST_ADS_TAB: AdsTabId = AD_STATE_TOKENS[0];

function isAdsTabId(value: string): value is AdsTabId {
  return (ADS_TAB_ORDER as readonly string[]).includes(value);
}

/** Resolves `?tab=` — absent, repeated, or unrecognised all fall back to {@link FIRST_ADS_TAB}
 * silently. */
export function resolveAdsTab(raw: string | string[] | undefined): AdsTabId {
  return typeof raw === "string" && isAdsTabId(raw) ? raw : FIRST_ADS_TAB;
}

/** Resolves `?limit=` — one of {@link ADS_PAGE_SIZES} or {@link DEFAULT_ADS_PAGE_SIZE}. */
export function resolveAdsPageSize(raw: string | string[] | undefined): AdsPageSize {
  if (typeof raw !== "string") return DEFAULT_ADS_PAGE_SIZE;
  const parsed = Number(raw);
  return (ADS_PAGE_SIZES as readonly number[]).includes(parsed) ? (parsed as AdsPageSize) : DEFAULT_ADS_PAGE_SIZE;
}

/** Resolves `?page=` — a positive integer, defaulting to 1 for anything absent, non-numeric, or
 * less than 1. Unclamped against the last page here — {@link resolveAdsPaging} applies the
 * separate Int32-overflow clamp once it knows `limit` too. */
export function resolveAdsPageNumber(raw: string | string[] | undefined): number {
  if (typeof raw !== "string") return 1;
  const parsed = Number.parseInt(raw, 10);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

/** `AdsController.List`'s own `offset` query parameter is a C# `int?` — mirrors
 * `clampGardenerPageForOffset` exactly (see that function's own remarks for the ASP.NET model-
 * binding reasoning). */
const INT32_MAX = 2147483647;

function clampAdsPageForOffset(page: number, limit: number): number {
  const maxPage = Math.floor(INT32_MAX / limit) + 1;
  return Math.min(page, maxPage);
}

/** Resolves the full `{ tab, page, limit, offset }` tuple `page.tsx` needs from one raw
 * searchParams object. */
export function resolveAdsPaging(sp: AdsSearchParams): ResolvedAdsPaging {
  const tab = resolveAdsTab(sp.tab);
  const limit = resolveAdsPageSize(sp.limit);
  const page = clampAdsPageForOffset(resolveAdsPageNumber(sp.page), limit);
  return { tab, page, limit, offset: (page - 1) * limit };
}

/** "Page N of M" from a state-scoped `total` — at least 1, even over an empty state, so a caller
 * never special-cases a zero total before dividing. */
export function resolveAdsPageCount(total: number, limit: number): number {
  return Math.max(1, Math.ceil(total / limit));
}

// ── Href builders ────────────────────────────────────────────────────────────────────────────
//
// `limit` rides a href only when it differs from the default, and `page` only past page 1 — the
// common case stays the cleanest URL (mirrors `assembleGardenerHref`).

function assembleAdsHref(tab: AdsTabId, limit: AdsPageSize, page?: number): string {
  const query = new URLSearchParams();
  if (tab !== FIRST_ADS_TAB) query.set("tab", tab);
  if (limit !== DEFAULT_ADS_PAGE_SIZE) query.set("limit", String(limit));
  if (page !== undefined && page > 1) query.set("page", String(page));
  const qs = query.toString();
  return qs ? `/ads?${qs}` : "/ads";
}

/** A same-page-reset link for a given tab+limit (the tab strip and the size picker's shared
 * builder — mirrors `buildGardenerHref`). */
export function buildAdsHref(tab: AdsTabId, limit: AdsPageSize): string {
  return assembleAdsHref(tab, limit);
}

/** A Previous/Next pager link — same tab and `limit`, the target `page`. */
export function buildAdsPageHref(tab: AdsTabId, limit: AdsPageSize, page: number): string {
  return assembleAdsHref(tab, limit, page);
}
