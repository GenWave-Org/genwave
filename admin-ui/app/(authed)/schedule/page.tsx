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

export default async function SchedulePage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

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

  // A failed schedule read used to silently degrade to an EMPTY editor — but an unreadable week is
  // NOT the legal all-music empty week (SPEC F91.4): letting the operator paint over a grid that
  // only LOOKS empty and then save would full-replace (wipe) whatever the store actually holds
  // (gh-#255's silent save-loss). Fail loudly instead; a 200 with zero segments still renders the
  // editor normally.
  if (scheduleResult.status === "rejected" || !scheduleResult.value.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Schedule</h1>
        <p className="mt-4 text-[0.85rem] text-danger" role="alert">
          Unable to load the schedule. Reload to try again — editing is disabled so a save can&apos;t
          overwrite the stored week with an empty one.
        </p>
      </main>
    );
  }

  const personas = (await personasResult.value.json()) as RosterPersonaDto[];
  const week = (await scheduleResult.value.json()) as ScheduleWeekDto;

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
