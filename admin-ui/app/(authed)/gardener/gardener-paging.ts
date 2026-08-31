// The Gardener page's own `?tab=&page=&limit=` resolution and href builders (SPEC F153.10 rider
// 2026-08-31; STORY-381, STORY-382; PLAN T387, gh-#654/#655/#657). Plain TypeScript, no JSX and no
// Next.js request APIs — the ONE place this page's searchParams get parsed and re-assembled, kept
// separate from `page.tsx` (an async Server Component Jest can't easily drive for every scenario)
// so every rule below is testable by calling a function, mirroring the extraction `catalog/page.tsx`
// keeps inline only because its own test harness (the tree-walker in catalog-pages.spec.ts) can
// afford to call the whole async page function per case.
//
// URL semantics (binding, SPEC F153.10 rider):
//  - tab: one of `GARDENER_KIND_ORDER`'s own tokens; absent, repeated, or unrecognised all fall
//    back to the FIRST kind in that order, silently — never a 400, never an error state.
//  - page: a positive integer; absent/non-numeric/less than 1 falls back to 1. Unclamped against
//    the kind's own last page — a page past the real end is a legal request (the kind's empty
//    state renders, the pager's own Previous link stays live). It IS clamped against Int32
//    overflow (T387 review MED-1): see `resolveGardenerPaging`'s own remarks.
//  - limit: one of `GARDENER_PAGE_SIZES`; absent or out-of-set falls back to
//    `DEFAULT_GARDENER_PAGE_SIZE` — the same "a paging value is a hint, never a contract a client
//    can get wrong" posture `GardenerController`'s own server-side clamp already takes.
//  - offset = (page - 1) * limit — a plain paging-unit count; whether that unit is rows or
//    near_duplicate GROUPS is `Garden.RotFindingRepository`'s own concern, not this page's.

import { GARDENER_KIND_ORDER, type GardenerKind } from "@/lib/gardener-api";

/** Raw `?tab=&page=&limit=` values exactly as Next.js hands them back — each MAY arrive as a
 * string array if the query string repeats the key. None of these three is ever meant to repeat,
 * but every resolver below treats a repeated value the same as an unrecognised one (falls back to
 * default) rather than picking one arbitrarily — mirrors `resolveWardrobeTab`'s own defensive
 * "absent, an array, or a stranger" posture (gh-#393). */
export interface GardenerSearchParams {
  tab?: string | string[];
  page?: string | string[];
  limit?: string | string[];
}

export const GARDENER_PAGE_SIZES = [25, 50, 100, 250] as const;
export type GardenerPageSize = (typeof GARDENER_PAGE_SIZES)[number];
export const DEFAULT_GARDENER_PAGE_SIZE: GardenerPageSize = 25;

export interface ResolvedGardenerPaging {
  tab: GardenerKind;
  page: number;
  limit: GardenerPageSize;
  offset: number;
}

/** The URL's own founding tab — bare `/gardener` and every tab-preserving href omit `?tab=`
 * entirely for this one kind, mirroring `CatalogTabs`' bare `/catalog` href for its own founding
 * "tracks" tab. `GARDENER_KIND_ORDER`'s own `as const satisfies` typing (T387 review LOW-3) makes
 * `[0]` a plain `GardenerKind` under `noUncheckedIndexedAccess` — no runtime empty-array guard
 * needed for a five-entry constant that is never actually empty. */
const FIRST_GARDENER_TAB: GardenerKind = GARDENER_KIND_ORDER[0];

function isGardenerKind(value: string): value is GardenerKind {
  return (GARDENER_KIND_ORDER as readonly string[]).includes(value);
}

/** Resolves `?tab=` (SPEC F153.10 rider, STORY-381 AC1/AC7) — absent, repeated, or unrecognised
 * all fall back to {@link FIRST_GARDENER_TAB} silently. */
export function resolveGardenerTab(raw: string | string[] | undefined): GardenerKind {
  return typeof raw === "string" && isGardenerKind(raw) ? raw : FIRST_GARDENER_TAB;
}

/** Resolves `?limit=` — one of {@link GARDENER_PAGE_SIZES} or {@link DEFAULT_GARDENER_PAGE_SIZE}.
 * An out-of-set value (e.g. `?limit=999`) reads as the default rather than 400ing. */
export function resolveGardenerPageSize(raw: string | string[] | undefined): GardenerPageSize {
  if (typeof raw !== "string") return DEFAULT_GARDENER_PAGE_SIZE;
  const parsed = Number(raw);
  return (GARDENER_PAGE_SIZES as readonly number[]).includes(parsed)
    ? (parsed as GardenerPageSize)
    : DEFAULT_GARDENER_PAGE_SIZE;
}

/** Resolves `?page=` — a positive integer, defaulting to 1 for anything absent, non-numeric, or
 * less than 1. Unclamped against the kind's own last page here — {@link resolveGardenerPaging}
 * applies the separate Int32-overflow clamp once it knows `limit` too. */
export function resolveGardenerPageNumber(raw: string | string[] | undefined): number {
  if (typeof raw !== "string") return 1;
  const parsed = Number.parseInt(raw, 10);
  return Number.isInteger(parsed) && parsed >= 1 ? parsed : 1;
}

/** `GardenerController`'s own `offset` query parameter is a C# `int?` — a derived offset beyond
 * `Int32.MaxValue` fails ASP.NET model binding, and `[ApiController]`'s automatic validation turns
 * that into a 400 (T387 review MED-1). SPEC F153.10 rider's "a paging value is a hint, never a
 * contract a client can get wrong" promise means an absurd `?page=` must degrade — same as an
 * out-of-set `?limit=` — rather than error, so the resolved `page` is capped at the largest value
 * whose `(page - 1) * limit` still fits in Int32. */
const INT32_MAX = 2147483647;

function clampGardenerPageForOffset(page: number, limit: number): number {
  const maxPage = Math.floor(INT32_MAX / limit) + 1;
  return Math.min(page, maxPage);
}

/** Resolves the full `{ tab, page, limit, offset }` tuple `page.tsx` needs from one raw
 * searchParams object — the single call site composing the resolvers above, plus the Int32-overflow
 * clamp {@link clampGardenerPageForOffset} needs `limit` for. */
export function resolveGardenerPaging(sp: GardenerSearchParams): ResolvedGardenerPaging {
  const tab = resolveGardenerTab(sp.tab);
  const limit = resolveGardenerPageSize(sp.limit);
  const page = clampGardenerPageForOffset(resolveGardenerPageNumber(sp.page), limit);
  return { tab, page, limit, offset: (page - 1) * limit };
}

/** "Page N of M" from a kind-scoped `total` (STORY-382 AC6/AC8's own EXACT per-kind count — GROUPS
 * for `near_duplicate`, ROWS for every other kind, per `GardenerController`'s own remarks) — never
 * derived from `/api/status`'s own OPEN-only count, which answers a different question entirely
 * (SPEC F153.10 rider). At least 1, even over an empty kind, so a caller never special-cases a
 * zero total before dividing. */
export function resolveGardenerPageCount(total: number, limit: number): number {
  return Math.max(1, Math.ceil(total / limit));
}

// ── Href builders ────────────────────────────────────────────────────────────────────────────
//
// `limit` rides a href only when it differs from the default, and `page` only past page 1 — the
// common case stays the cleanest URL, mirroring `CatalogTabs`' own "founding tab omits ?tab="
// convention.

function assembleGardenerHref(kind: GardenerKind, limit: GardenerPageSize, page?: number): string {
  const query = new URLSearchParams();
  if (kind !== FIRST_GARDENER_TAB) query.set("tab", kind);
  if (limit !== DEFAULT_GARDENER_PAGE_SIZE) query.set("limit", String(limit));
  if (page !== undefined && page > 1) query.set("page", String(page));
  const qs = query.toString();
  return qs ? `/gardener?${qs}` : "/gardener";
}

/** A same-page-reset link for a given kind+limit (STORY-381 AC3, STORY-382 AC3-AC4) — the ONE
 * builder for both the tab strip (switching kind, same `limit`) and the size picker (same kind,
 * switching `limit`): both are, structurally, "the href for this kind+limit combination, page
 * reset to 1" (T387 review LOW-1 — `buildGardenerTabHref` and `buildGardenerLimitHref` computed
 * byte-identical output and are collapsed into this one name). */
export function buildGardenerHref(kind: GardenerKind, limit: GardenerPageSize): string {
  return assembleGardenerHref(kind, limit);
}

/** A Previous/Next pager link (STORY-382 AC1-AC5, AC7) — same tab and `limit`, the target `page`. */
export function buildGardenerPageHref(kind: GardenerKind, limit: GardenerPageSize, page: number): string {
  return assembleGardenerHref(kind, limit, page);
}
