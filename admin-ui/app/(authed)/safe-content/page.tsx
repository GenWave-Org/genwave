import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import type { LibraryDto } from "@/lib/library";
import type { ImagingShowOption } from "./imaging-show-scope";
import { SafeContentClient } from "./SafeContentClient";
import type { SafeSegmentDto } from "./SafeContentClient";

// SPEC F27.10 — Station:Safe:SeedMessage is a generation-time input, not a live-editable
// setting, and is deliberately excluded from GET /api/settings (no API returns it). This
// default MUST stay in sync with StationSafeOptions.SeedMessage's default in
// src/GenWave.Host/Options/StationSafeOptions.cs.
const DEFAULT_SEED_MESSAGE =
  "You're listening to {StationName}. We'll be right back — stay tuned.";
const DEFAULT_TITLE = "Please Stand By";
const SAFE_LIBRARY_NAME = "safe";

// Segments are authored on demand and eligibility is toggled live — always render fresh.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

/** Picks the library named "safe" (SafeLoopSeeder.SafeLibraryName) when present, else the first library. */
function resolveDefaultLibraryId(libraries: LibraryDto[]): number | null {
  const safeLibrary = libraries.find(
    (lib) => lib.name.toLowerCase() === SAFE_LIBRARY_NAME
  );
  if (safeLibrary !== undefined) return safeLibrary.id;
  return libraries[0]?.id ?? null;
}

/** The two fields this page's projection reads off `GET /api/shows`'s full `ShowDto` — mirrors the
 * schedule page's own `ScheduleShowWireRow` local-not-imported posture (PLAN T245's own precedent):
 * this folder has no reason to know that DTO's full shape. */
interface ImagingShowWireRow {
  id: number;
  name: string;
}

/** Projects a settled `GET /api/shows` result down to the scope picker's roster (SPEC F117.1/
 * F119.4, PLAN T246) — empty roster on anything but a 200 (a rejected promise, same as a non-2xx
 * response), never thrown: the scope picker is a minimal, optional delta on this editor (F119.4),
 * not load-bearing the way the libraries fetch below is. Mirrors schedule/page.tsx's own
 * `deriveShowsStatus` PromiseSettledResult posture.
 *
 * `GET /api/shows` lives on the Settings/format-clock plane, not this page's own Operator plane
 * (F27's surface) — reading it here couples the two; a future RBAC split that separates them would
 * empty this picker silently rather than error loudly, which is the intended degrade for now (the
 * picker is optional, per the remarks above) but worth naming for whoever draws that boundary. */
async function deriveShowOptions(result: PromiseSettledResult<Response>): Promise<ImagingShowOption[]> {
  if (result.status === "rejected" || !result.value.ok) return [];
  const rows = (await result.value.json()) as ImagingShowWireRow[];
  return rows.map((row) => ({ id: row.id, name: row.name }));
}

export default async function SafeContentPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  // Promise.allSettled, not sequential awaits: a rejected fetch (network error, DNS, ...) must
  // never throw out of this Server Component and 500 the whole page (there's no error.tsx here) —
  // mirrors schedule/page.tsx and personas/page.tsx's own posture. The two reads are independent
  // (shows doesn't need libraries), so they run in parallel rather than one after the other.
  const [librariesResult, showsResult] = await Promise.allSettled([
    apiGet("/api/libraries", { cookies: cookieHeader }),
    apiGet("/api/shows", { cookies: cookieHeader }),
  ]);

  if (librariesResult.status === "rejected" || !librariesResult.value.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Station Imaging</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load libraries.</p>
      </main>
    );
  }

  const libraries = (await librariesResult.value.json()) as LibraryDto[];
  const defaultLibraryId = resolveDefaultLibraryId(libraries);
  const shows = await deriveShowOptions(showsResult);

  let initialSegments: SafeSegmentDto[] = [];
  let initialOutOfScope = false;

  if (defaultLibraryId !== null) {
    // Explicit library-id (F23.2) — the safe library may sit outside Station:Scope:LibraryIds,
    // so an unnamed browse (bounded by station scope) would come back empty.
    const mediaResp = await apiGet(`/api/media?library-id=${defaultLibraryId}&limit=200`, {
      cookies: cookieHeader,
    });
    if (mediaResp.ok) {
      initialSegments = (await mediaResp.json()) as SafeSegmentDto[];
      initialOutOfScope = mediaResp.headers.get("X-Out-Of-Scope") === "true";
    }
  }

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Station Imaging</h1>
      {/* gh-#149 — "always airable" moved out of the old "Safe content" name into this one help
          sentence: the never-dead-air guarantee, stated where the rename dropped it. */}
      <p className="mt-1 text-[0.85rem] text-mute">
        Station IDs, jingles, sweepers, liners — always airable: when the music rotation drains,
        these segments keep the station on air.
      </p>
      <div className="mt-4">
        <SafeContentClient
          libraries={libraries}
          initialLibraryId={defaultLibraryId}
          initialSegments={initialSegments}
          initialOutOfScope={initialOutOfScope}
          defaultText={DEFAULT_SEED_MESSAGE}
          defaultTitle={DEFAULT_TITLE}
          shows={shows}
        />
      </div>
    </main>
  );
}
