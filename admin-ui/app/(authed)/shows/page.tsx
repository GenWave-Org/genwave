import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { ShowsClient } from "./ShowsClient";
import type { ShowDto } from "./types";

// Shows are authored/edited right here and read live elsewhere (the schedule editor's show picker,
// PLAN T243) — always re-render from the server, mirroring personas/page.tsx and wardrobe/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

export default async function ShowsPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();
  const response = await apiGet("/api/shows", { cookies: cookieHeader });

  if (!response.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Shows</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load shows.</p>
      </main>
    );
  }

  const shows = (await response.json()) as ShowDto[];

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Shows</h1>
      <div className="mt-4">
        <ShowsClient initialShows={shows} />
      </div>
    </main>
  );
}
