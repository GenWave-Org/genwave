import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import type { LibraryDto } from "@/lib/library";
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

export default async function SafeContentPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  const librariesResp = await apiGet("/api/libraries", { cookies: cookieHeader });

  if (!librariesResp.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Safe content</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load libraries.</p>
      </main>
    );
  }

  const libraries = (await librariesResp.json()) as LibraryDto[];
  const defaultLibraryId = resolveDefaultLibraryId(libraries);

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
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Safe content</h1>
      <div className="mt-4">
        <SafeContentClient
          libraries={libraries}
          initialLibraryId={defaultLibraryId}
          initialSegments={initialSegments}
          initialOutOfScope={initialOutOfScope}
          defaultText={DEFAULT_SEED_MESSAGE}
          defaultTitle={DEFAULT_TITLE}
        />
      </div>
    </main>
  );
}
