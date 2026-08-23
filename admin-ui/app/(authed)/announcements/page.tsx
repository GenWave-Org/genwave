import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import type { AnnounceTokenStatusDto, AnnouncementHistoryDto } from "@/lib/announcements-api";
import { AnnouncementsClient } from "./AnnouncementsClient";

// History states change server-side (the lifecycle guardians, PLAN T343) independent of any admin
// action — always re-render from the server, mirroring safe-content/page.tsx and settings/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

/** The one field this page's projection reads off `GET /api/settings`'s full `SettingDto` (SPEC
 * F146.3's public-mode notice) — mirrors safe-content/page.tsx's own `ImagingShowWireRow` local-not-
 * imported posture: this folder has no reason to know that DTO's full shape. */
interface SpectatorModeSettingRow {
  key: string;
  value: string;
}

const SPECTATOR_MODE_KEY = "Station:SpectatorMode";

/** Projects `GET /api/settings`'s rows down to the one boolean this page needs (SPEC F146.3) — the
 * caller already normalized a rejected/non-2xx read down to an empty `rows` array, so an empty array
 * finding nothing and degrading to `false` here (the composer renders, and a station that really is
 * public still gets the server's own honest 403 on send) covers that case too, with no separate
 * failure branch to keep in step: the notice is a UX courtesy, not the security boundary —
 * `AnnouncementsController.Post` enforces F145.1 regardless of what this page believes. */
function deriveSpectatorMode(rows: SpectatorModeSettingRow[]): boolean {
  return rows.find((row) => row.key === SPECTATOR_MODE_KEY)?.value === "true";
}

export default async function AnnouncementsPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  // Three independent reads — Promise.allSettled so a single failed fetch never 500s the whole page
  // (mirrors safe-content/page.tsx's own posture): the composer's spectator-mode notice, the history
  // list, and the token panel's status can each degrade on their own.
  const [settingsResult, historyResult, tokenStatusResult] = await Promise.allSettled([
    apiGet("/api/settings", { cookies: cookieHeader }),
    apiGet("/api/announcements", { cookies: cookieHeader }),
    apiGet("/api/announcements/token/status", { cookies: cookieHeader }),
  ]);

  const settingsRows =
    settingsResult.status === "fulfilled" && settingsResult.value.ok
      ? ((await settingsResult.value.json()) as SpectatorModeSettingRow[])
      : [];
  const spectatorMode = deriveSpectatorMode(settingsRows);

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
          initialSpectatorMode={spectatorMode}
          initialHistory={initialHistory}
          initialTokenStatus={initialTokenStatus}
        />
      </div>
    </main>
  );
}
