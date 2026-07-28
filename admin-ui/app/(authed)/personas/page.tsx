import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { PersonasClient } from "./PersonasClient";
import type { PersonaDto } from "./types";

// Personas, the format-clock schedule, and on-air status are all live/mutable (an operator edits
// personas here and the schedule elsewhere; the clock ticks on its own) — always re-render from
// the server, mirroring safe-content/page.tsx and settings/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

/** Shape of one `GET /api/schedule` segment row this page reads — only `personaId`, to derive the
 * Scheduled/Bench split (SPEC F94.1, STORY-246, PLAN T127). The grid itself (day/minute/genre/
 * energy) is the T129 schedule editor's own concern, not this page's. */
interface ScheduleSegmentRow {
  personaId: number | null;
}

/** Shape of `GET /api/schedule`'s week document this page reads. */
interface ScheduleWeekRow {
  segments: ScheduleSegmentRow[];
}

/** Shape of `GET /api/status` this page reads (SPEC F91.5) — only the resolver-sourced on-air
 * persona NAME. `/api/status` carries no persona id (`StatusController`'s `llm.activePersona` is
 * `persona?.Name`), so the On The Air badge matches roster rows by name rather than id; that's an
 * honest match, not a fuzzy one — `station.persona.name` is `unique` (db/09-persona-migration.sh).
 * `llm` itself is optional here (not just `activePersona`): this is an unvalidated cast of a 2xx
 * body, and reading it via `?.` below means a shape surprise degrades to "no on-air badge" rather
 * than throwing mid-render. */
interface StatusRow {
  llm?: { activePersona: string | null };
}

/** Every persona id appearing in ≥1 schedule segment (STORY-246) — the Scheduled/Bench split. A
 * music-only segment (`personaId: null`, SPEC F91.4) contributes nothing. `week.segments` is
 * guarded with `Array.isArray` — this is an unvalidated cast of a 2xx body, so a malformed/missing
 * `segments` field degrades to "no schedule data" rather than throwing mid-render. */
function scheduledPersonaIdsFrom(week: ScheduleWeekRow): number[] {
  const ids = new Set<number>();
  const segments = Array.isArray(week.segments) ? week.segments : [];
  for (const segment of segments) {
    if (segment.personaId !== null) ids.add(segment.personaId);
  }
  return [...ids];
}

export default async function PersonasPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  // Promise.allSettled, not Promise.all: the schedule/status reads are both optional degrades
  // (see their own remarks below) — a network reject on either must never take the personas read
  // down with it and 500 the whole page.
  const [personasResult, scheduleResult, statusResult] = await Promise.allSettled([
    apiGet("/api/personas", { cookies: cookieHeader }),
    apiGet("/api/schedule", { cookies: cookieHeader }),
    apiGet("/api/status", { cookies: cookieHeader }),
  ]);

  if (personasResult.status === "rejected" || !personasResult.value.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Personas</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load personas.</p>
      </main>
    );
  }

  const personas = (await personasResult.value.json()) as PersonaDto[];

  // The Scheduled/Bench split degrades to "everyone benched" rather than failing the whole page
  // when the schedule can't be read (a network reject or a non-2xx alike) — a schedule-less
  // render is always legal (SPEC F91.4).
  const scheduledPersonaIds =
    scheduleResult.status === "fulfilled" && scheduleResult.value.ok
      ? scheduledPersonaIdsFrom((await scheduleResult.value.json()) as ScheduleWeekRow)
      : [];

  // The On The Air badge degrades to "none" rather than failing the whole page when status can't
  // be read (a network reject or a non-2xx alike), the same posture the retired active-id badge
  // used to take on a failed settings read.
  const onAirPersonaName =
    statusResult.status === "fulfilled" && statusResult.value.ok
      ? ((await statusResult.value.json()) as StatusRow).llm?.activePersona ?? null
      : null;

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Personas</h1>
      <div className="mt-4">
        <PersonasClient
          initialPersonas={personas}
          scheduledPersonaIds={scheduledPersonaIds}
          onAirPersonaName={onAirPersonaName}
        />
      </div>
    </main>
  );
}
