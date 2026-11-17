import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import type { AnnounceTokenStatusDto, AnnouncementHistoryDto } from "@/lib/announcements-api";
import { AnnouncementsClient } from "./AnnouncementsClient";

// History states change server-side (the lifecycle guardians, PLAN T343) independent of any admin
// action — always re-render from the server, mirroring safe-content/page.tsx and settings/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

export default async function AnnouncementsPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  // Two independent reads — Promise.allSettled so a single failed fetch never 500s the whole page
  // (mirrors safe-content/page.tsx's own posture): the history list and the token panel's status can
  // each degrade on their own.
  const [historyResult, tokenStatusResult] = await Promise.allSettled([
    apiGet("/api/announcements", { cookies: cookieHeader }),
    apiGet("/api/announcements/token/status", { cookies: cookieHeader }),
  ]);

  const initialHistory: AnnouncementHistoryDto[] =
    historyResult.status === "fulfilled" && historyResult.value.ok
      ? ((await historyResult.value.json()) as AnnouncementHistoryDto[])
      : [];

  const initialTokenStatus: AnnounceTokenStatusDto =
    tokenStatusResult.status === "fulfilled" && tokenStatusResult.value.ok
      ? ((await tokenStatusResult.value.json()) as AnnounceTokenStatusDto)
      : { hasToken: false, lastUsedAt: null };

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Announcements</h1>
      <p className="mt-1 text-[0.85rem] text-mute">
        Type a message and the on-air DJ delivers it next break — flavored in character, or word for
        word.
      </p>
      <div className="mt-4">
        <AnnouncementsClient
          initialHistory={initialHistory}
          initialTokenStatus={initialTokenStatus}
        />
      </div>
    </main>
  );
}
