import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { PersonasClient } from "./PersonasClient";
import type { PersonaDto } from "./types";

// Personas and the active-DJ pointer are both mutable (operators write via this page); always
// re-render from the server, mirroring safe-content/page.tsx and settings/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

/** The one F19 allowlist key this page reads to seed the initial active badge (SPEC F35.2). */
const ACTIVE_ID_KEY = "Station:Persona:ActiveId";

/** Shape of a `GET /api/settings` row — only the fields this page reads. */
interface SettingRow {
  key: string;
  value: string;
}

/** Resolves the active persona id from the F19 settings overlay; `0`/unset/unparsable = none. */
function resolveActiveId(settings: SettingRow[]): number {
  const row = settings.find((s) => s.key === ACTIVE_ID_KEY);
  if (row === undefined) return 0;
  const parsed = Number(row.value);
  return Number.isFinite(parsed) ? parsed : 0;
}

export default async function PersonasPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  const [personasResp, settingsResp] = await Promise.all([
    apiGet("/api/personas", { cookies: cookieHeader }),
    apiGet("/api/settings", { cookies: cookieHeader }),
  ]);

  if (!personasResp.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Personas</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load personas.</p>
      </main>
    );
  }

  const personas = (await personasResp.json()) as PersonaDto[];

  // The active-id badge degrades to "none" rather than failing the whole page when settings
  // can't be read — a persona-less render is always legal (SPEC F35.2).
  const settings = settingsResp.ok ? ((await settingsResp.json()) as SettingRow[]) : [];
  const activeId = resolveActiveId(settings);

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Personas</h1>
      <div className="mt-4">
        <PersonasClient initialPersonas={personas} initialActiveId={activeId} />
      </div>
    </main>
  );
}
