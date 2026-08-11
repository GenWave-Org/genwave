import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { ScheduleEditor } from "./ScheduleEditor";
import { SpecialsForm } from "./SpecialsForm";
import type {
  RosterPersonaDto,
  ScheduleShowOptionDto,
  ScheduleShowsStatus,
  ScheduleSpecialDto,
  ScheduleSpecialsStatus,
  ScheduleWeekDto,
} from "./types";

// The format-clock schedule is live/mutable station configuration (an operator paints it here,
// the clock ticks against it elsewhere) — always re-render from the server, mirroring
// personas/page.tsx and settings/page.tsx.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

/** The one field `GET /api/shows` returns that this projection reads besides `id`/`name`/`tagline` —
 * everything else on the wire's full `ShowDto` (`slug`, `flavor`, provenance) is simply never
 * mentioned here, so it can't leak into the object literal {@link deriveShowsStatus} builds (see
 * `ScheduleShowOptionDto`'s own remarks in `types.ts` for why that matters for `flavor`
 * specifically). Declared locally, not imported from `../shows/types`'s `ShowDto` — this folder has
 * no reason to know that DTO's full shape, the same "narrower fixture, no accidental drift" posture
 * `RosterPersonaDto`'s own comment already takes. */
interface ScheduleShowWireRow {
  id: number;
  name: string;
  tagline: string | null;
}

/** Projects a settled `GET /api/shows` result down to {@link ScheduleShowsStatus} (PLAN T245's P5/
 * P6): loaded-and-narrowed on a 200, `"error"` on anything else (a rejected promise, a non-2xx
 * response) — never thrown, since an unreadable show roster degrades the grid's show picker alone
 * (SPEC F119.3), it doesn't block the schedule page the way a failed personas/schedule load does. */
async function deriveShowsStatus(result: PromiseSettledResult<Response>): Promise<ScheduleShowsStatus> {
  if (result.status === "rejected" || !result.value.ok) return { kind: "error" };
  const rows = (await result.value.json()) as ScheduleShowWireRow[];
  const shows: ScheduleShowOptionDto[] = rows.map((row) => ({ id: row.id, name: row.name, tagline: row.tagline }));
  return { kind: "loaded", shows };
}

/** Projects a settled `GET /api/schedule/specials` result down to {@link ScheduleSpecialsStatus}
 * (PLAN T259) — mirrors {@link deriveShowsStatus}'s exact shape: loaded-and-narrowed on a 200,
 * `"error"` on anything else, never thrown. An unreadable specials list degrades `SpecialsForm`'s own
 * list section alone (SPEC F120's droppable-tail posture) — it never blocks the paint grid this page
 * exists for. */
async function deriveSpecialsStatus(result: PromiseSettledResult<Response>): Promise<ScheduleSpecialsStatus> {
  if (result.status === "rejected" || !result.value.ok) return { kind: "error" };
  const specials = (await result.value.json()) as ScheduleSpecialDto[];
  return { kind: "loaded", specials };
}

export default async function SchedulePage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieHeader = cookieStore.toString();

  const [personasResult, scheduleResult, showsResult, specialsResult] = await Promise.allSettled([
    apiGet("/api/personas", { cookies: cookieHeader }),
    apiGet("/api/schedule", { cookies: cookieHeader }),
    apiGet("/api/shows", { cookies: cookieHeader }),
    apiGet("/api/schedule/specials", { cookies: cookieHeader }),
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
  const shows = await deriveShowsStatus(showsResult);
  const specials = await deriveSpecialsStatus(specialsResult);

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Schedule</h1>
      <p className="mt-1 text-[0.85rem] text-mute">
        Select a DJ from the roster, then drag across the grid to paint their slots.
      </p>
      <div className="mt-4">
        <ScheduleEditor initialWeek={week} personas={personas} shows={shows} />
      </div>
      <div className="mt-8">
        <SpecialsForm personas={personas} shows={shows} specials={specials} />
      </div>
    </main>
  );
}
