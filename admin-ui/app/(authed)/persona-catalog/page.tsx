import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { PersonaCatalogClient } from "./PersonaCatalogClient";
import type { CatalogIndexResponseDto } from "./types";

// The underlying Community:CatalogIndexUrl setting is live-editable from Settings (SPEC F90.1),
// and the shelf itself can flip disabled<->enabled or gain/lose entries between visits — always
// re-render from the server, mirroring personas/page.tsx and safe-content/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

export default async function PersonaCatalogPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const response = await apiGet("/api/catalog/index", { cookies: cookieStore.toString() });

  // Disabled (SPEC F90.1): CatalogController serves a bare, zero-byte 404 here — the same
  // per-resource 404 shape MediaDetailPage's own "Not found" branch renders inline for (house
  // pattern, catalog/[mediaId]/page.tsx), rather than Next's framework notFound() (unused
  // anywhere else in this codebase). The Persona Catalog nav entry is ALSO hidden on this same
  // signal (see the authed layout) — this inline render only covers a direct/bookmarked visit.
  if (response.status === 404) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Not found</h1>
        <p className="mt-2 text-[0.85rem] text-mute">This page isn&apos;t available.</p>
      </main>
    );
  }

  if (!response.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Community Catalog</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load the community catalog.</p>
      </main>
    );
  }

  const index = (await response.json()) as CatalogIndexResponseDto;

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Community Catalog</h1>
      <div className="mt-4">
        <PersonaCatalogClient initialIndex={index} />
      </div>
    </main>
  );
}
