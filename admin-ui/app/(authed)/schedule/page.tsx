import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { ScheduleEditor } from "./ScheduleEditor";
import type { RosterPersonaDto, ScheduleWeekDto } from "./types";

// The format-clock schedule is live/mutable station configuration (an operator paints it here,
// the clock ticks against it elsewhere) — always re-render from the server, mirroring
// personas/page.tsx and settings/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

const EMPTY_WEEK: ScheduleWeekDto = { segments: [] };

export default async function SchedulePage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  // Promise.allSettled, not Promise.all: an unreadable schedule is a LEGAL state (SPEC F91.4, the
  // pre-clock/all-music week) — a network reject on it must never take the whole page down with
  // it. The persona roster is the one read this page can't degrade around (there's nothing to
  // paint with), so it alone gets the hard failure branch below.
  const [personasResult, scheduleResult] = await Promise.allSettled([
    apiGet("/api/personas", { cookies: cookieHeader }),
    apiGet("/api/schedule", { cookies: cookieHeader }),
  ]);

  if (personasResult.status === "rejected" || !personasResult.value.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Schedule</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load the persona roster.</p>
      </main>
    );
  }

  const personas = (await personasResult.value.json()) as RosterPersonaDto[];
  const week =
    scheduleResult.status === "fulfilled" && scheduleResult.value.ok
      ? ((await scheduleResult.value.json()) as ScheduleWeekDto)
      : EMPTY_WEEK;

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Schedule</h1>
      <p className="mt-1 text-[0.85rem] text-mute">
        Select a DJ from the roster, then drag across the grid to paint their slots.
      </p>
      <div className="mt-4">
        <ScheduleEditor initialWeek={week} personas={personas} />
      </div>
    </main>
  );
}
