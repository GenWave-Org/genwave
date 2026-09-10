// Client-side wire shapes for the Sponsors admin surface (SPEC F171.2, F171.7, F171.8; STORY-407,
// STORY-412, STORY-413; PLAN T447) — GET /api/sponsors' own row shape (SponsorListItemDto, the Ads
// page's SponsorRail) and the small cross-reference (SponsorRefDto) every ad spot/brief already
// carries under its own `sponsor` field. A separate module from ads-api.ts (that file's own header
// says it is "the ONE source for every Ads wire shape" — a sponsor is the customer an ad names, not
// an ad itself), mirroring the shows-rotation-api.ts/gardener-api.ts precedent this folder already
// holds for every other feature. Zero imports of its own: neither shape needs anything ads-api.ts
// exports, so this module stays a leaf the rest of the folder can depend on in one direction only.
// No fetchers here yet — T447 only ever READS this list, server-side via `apiGet` (`page.tsx`'s own
// convention, the `AD_BRIEFS_PATH` precedent one file over) — sponsor CRUD lands with whichever
// later task builds a sponsor-authoring surface.

/** `GET /api/sponsors`'s own path (SPEC F171.2) — a bare, unfiltered array, no query string from
 * this page (the `q=` fold-filter is a sponsor-authoring concern the rail doesn't need). */
export const SPONSORS_PATH = "/api/sponsors";

/** `SponsorListItemDto`'s exact wire shape (`GenWave.Host.Api.SponsorListItemDto`, SPEC F171.2;
 * PLAN T434) — every sponsor fact plus the referencing counts the rail displays. `spots` is keyed
 * by `ads-api.ts`'s own lowercase `AdState` tokens (kept a bare `Record<string, number>` here
 * rather than importing `AdState` — the rail only ever sums the values, never branches on a
 * particular key, so this module has no reason to know that type exists); a state with zero spots
 * is simply absent from the map, never an explicit zero entry. */
export interface SponsorListItemDto {
  id: number;
  name: string;
  packSlug: string | null;
  paused: boolean;
  pausedAt: string | null;
  tagline: string | null;
  about: string | null;
  phone: string | null;
  address: string | null;
  website: string | null;
  tone: string | null;
  createdAt: string;
  updatedAt: string;
  briefs: number;
  spots: Record<string, number>;
  shows: number;
}

/** `SponsorRefDto`'s exact wire shape (`GenWave.Host.Api.SponsorRefDto`) — the small cross-
 * reference `AdSpotDto.sponsor`/`AdBriefDto.sponsor` ride, and what every sponsor `<select>` picker
 * on this page's forms is built from. Never a free-text customer label — id/name for display,
 * `paused` so a picker can grey out (or flag) a paused sponsor without a second round trip. */
export interface SponsorRefDto {
  id: number;
  name: string;
  paused: boolean;
}
