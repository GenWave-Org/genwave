import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { EditorClient } from "./EditorClient";
import type { AssignableFaceDto, ThemeSummaryDto } from "./types";

// A theme/pack can be saved/installed elsewhere at any time — always re-render from the server,
// mirroring wardrobe/page.tsx and persona-catalog/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

/** Fetches one JSON-array GET endpoint, degrading to `[]` on any failure (network error, non-200) —
 * fail closed, mirroring `wardrobe/page.tsx`'s own `fetchCatalogEnabled` posture: no live signal
 * means nothing gets offered. This page's own two GET routes (`/api/themes`, `/api/fonts/assignable`)
 * shared this exact try/if/catch shape byte-for-byte before this fix (review finding N3) — one
 * generic helper instead of two (formerly three, before `GET /api/fonts` dropped out of this page
 * entirely — see `EditorClient`'s own remarks, review finding F4) copies that could only ever drift
 * apart. */
async function fetchListOrEmpty<T>(path: string, cookieHeader: string): Promise<T[]> {
  try {
    const response = await apiGet(path, { cookies: cookieHeader });
    if (!response.ok) return [];
    return (await response.json()) as T[];
  } catch {
    return [];
  }
}

export default async function EditorPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();
  const [themes, assignableFaces] = await Promise.all([
    fetchListOrEmpty<ThemeSummaryDto>("/api/themes", cookieHeader),
    fetchListOrEmpty<AssignableFaceDto>("/api/fonts/assignable", cookieHeader),
  ]);

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Editor</h1>
      <div className="mt-4">
        <EditorClient themes={themes} assignableFaces={assignableFaces} />
      </div>
    </main>
  );
}
