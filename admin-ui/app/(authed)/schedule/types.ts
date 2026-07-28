/** Wire shape of one row inside `GET/PUT /api/schedule`'s week document — mirrors
 * `GenWave.Host.Api.ScheduleSegmentDto` field for field (SPEC F91.1, F91.8; STORY-240, PLAN T122).
 * `day` is 0-6, Sunday=0 (the wire's own numbering, matching `System.DayOfWeek`). `id` is populated
 * on every row a GET returns and is IGNORED by the server on a PUT body (T122's own documented
 * "PUT always treats the week as brand-new rows" contract) — this editor never reads or round-trips
 * it, it just sends `null` on every submitted row. `personaId: null` is the music-only segment (no
 * DJ). `genres`/`energyMin`/`energyMax` all `null` means "station default" for that block. */
export interface ScheduleSegmentDto {
  id: number | null;
  day: number;
  startMinute: number;
  endMinute: number;
  personaId: number | null;
  genres: string[] | null;
  energyMin: number | null;
  energyMax: number | null;
}

/** The whole-week document body shared by GET and PUT `/api/schedule` — mirrors
 * `GenWave.Host.Api.ScheduleWeekDto`. Zero `segments` is legal (the pre-clock, all-music week,
 * SPEC F91.4). */
export interface ScheduleWeekDto {
  segments: ScheduleSegmentDto[];
}

/** One entry of a `PUT /api/schedule` 400's `cellErrors` extension — mirrors
 * `GenWave.Host.Api.ScheduleCellErrorDto` field for field. `day`/`startMinute`/`endMinute` are the
 * same three wire fields the offending submitted row carried, which is exactly enough for this
 * editor to re-find the block it belongs to without needing `rowIndex` (see
 * `schedule-grid-model.ts`'s `cellErrorMatchesRun`). */
export interface ScheduleCellErrorDto {
  rowIndex: number;
  day: number;
  startMinute: number;
  endMinute: number;
  kind: string;
  message: string;
}

/** The slice of `GET /api/personas` this page needs for the roster palette — just enough to paint
 * and label a block. Deliberately narrower than the full `PersonaDto` (`../personas/types`): this
 * folder has no reason to know about backstory/style/voice/provenance, and a narrower shape means a
 * fixture here can't accidentally drift onto fields this editor never reads. */
export interface RosterPersonaDto {
  id: number;
  name: string;
}
