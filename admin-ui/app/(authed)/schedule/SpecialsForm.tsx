"use client";

import { useId, useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { EmptyState } from "@/components/ui/empty-state";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { formatHalfHourLabel, formatRunTimeRange, MINUTES_PER_HALF_HOUR } from "./schedule-grid-model";
import type { RosterPersonaDto, ScheduleShowsStatus, ScheduleSpecialDto, ScheduleSpecialsStatus } from "./types";

export interface SpecialsFormProps {
  /** The persona roster — the schedule page already loads this for the paint palette (PLAN T259:
   * "persona options = the roster, the page already loads personas"). */
  personas: readonly RosterPersonaDto[];
  /** The show roster — the schedule page already loads this for the grid side panel (PLAN T259:
   * "show options = the shows list, the page already loads shows post-T245!"). */
  shows: ScheduleShowsStatus;
  /** `GET /api/schedule/specials`'s own load state, fetched once server-side alongside
   * personas/schedule/shows (mirrors {@link ScheduleShowsStatus}'s exact posture). */
  specials: ScheduleSpecialsStatus;
}

interface FormValues {
  onDate: string;
  startMinute: number;
  endMinute: number;
  personaId: string;
  showId: string;
  genresText: string;
  energyMinText: string;
  energyMaxText: string;
}

const DEFAULT_START_MINUTE = 18 * 60;
const DEFAULT_END_MINUTE = 20 * 60;

const EMPTY_FORM: FormValues = {
  onDate: "",
  startMinute: DEFAULT_START_MINUTE,
  endMinute: DEFAULT_END_MINUTE,
  personaId: "",
  showId: "",
  genresText: "",
  energyMinText: "",
  energyMaxText: "",
};

/** Every legal `startMinute` (0, 30, …, 1410) and `endMinute` (30, 60, …, 1440) value, built once —
 * a `<select>` sourced from these two lists can only ever submit an on-grid, 30-minute-step value by
 * CONSTRUCTION (design principle: make the illegal state unrepresentable), so this form never needs
 * its own copy of the server's 30-minute-step/range check the way a free-text time field would. */
const START_MINUTE_OPTIONS: readonly number[] = Array.from(
  { length: 48 },
  (_, halfHour) => halfHour * MINUTES_PER_HALF_HOUR
);
const END_MINUTE_OPTIONS: readonly number[] = Array.from(
  { length: 48 },
  (_, halfHour) => (halfHour + 1) * MINUTES_PER_HALF_HOUR
);

/** `edit` carries the special's own id, frozen at `startEdit` time. There is no `PATCH
 * /api/schedule/specials/{id}` (SPEC F120.3's own "CRUD minimal" instruction) — an "edit" here is
 * DELETE-then-POST (see `handleSubmit`'s own remarks for the honest v1 tradeoff that shape carries). */
type FormMode = { kind: "create" } | { kind: "edit"; id: number };

/** Body accepted by `POST /api/schedule/specials` (mirrors `GenWave.Host.Api.SpecialRequestDto`). */
interface SpecialRequestBody {
  onDate: string;
  startMinute: number;
  endMinute: number;
  personaId: number | null;
  genres: string[] | null;
  energyMin: number | null;
  energyMax: number | null;
  showId: number | null;
}

function requestBodyFrom(form: FormValues): SpecialRequestBody {
  return {
    onDate: form.onDate,
    startMinute: form.startMinute,
    endMinute: form.endMinute,
    personaId: form.personaId === "" ? null : Number(form.personaId),
    showId: form.showId === "" ? null : Number(form.showId),
    genres: parseGenres(form.genresText),
    energyMin: parseEnergy(form.energyMinText),
    energyMax: parseEnergy(form.energyMaxText),
  };
}

/** Mirrors `ScheduleEnvelopePanel`'s own `parseGenres` — a per-file copy, not a shared import (this
 * folder's own established idiom: `ScheduleEnvelopePanel`/`ScheduleShowPicker` each carry their own
 * small field parsers rather than a shared utility module). Blank/all-whitespace text is the
 * station-default sentinel, `null` — never an empty array. */
function parseGenres(text: string): string[] | null {
  const entries = text
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry !== "");
  return entries.length === 0 ? null : entries;
}

/** Mirrors `ScheduleEnvelopePanel`'s own `parseEnergy`. */
function parseEnergy(text: string): number | null {
  const trimmed = text.trim();
  if (trimmed === "") return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function formValuesFromSpecial(special: ScheduleSpecialDto): FormValues {
  return {
    onDate: special.onDate,
    startMinute: special.startMinute,
    endMinute: special.endMinute,
    personaId: special.personaId === null ? "" : String(special.personaId),
    showId: special.showId === null ? "" : String(special.showId),
    genresText: special.genres?.join(", ") ?? "",
    energyMinText: special.energyMin?.toString() ?? "",
    energyMaxText: special.energyMax?.toString() ?? "",
  };
}

/** `"09:00–12:00"` from a pair of on-grid minute values — calls `schedule-grid-model`'s own
 * `formatRunTimeRange` (dividing by `MINUTES_PER_HALF_HOUR` first, since that function takes
 * half-hour INDICES, not minutes) rather than a bespoke formatter, so a special's span reads
 * identically to the paint grid's own runs, including its 24:00-not-00:00 midnight handling. */
function formatMinuteRange(startMinute: number, endMinute: number): string {
  return formatRunTimeRange(startMinute / MINUTES_PER_HALF_HOUR, endMinute / MINUTES_PER_HALF_HOUR);
}

function bySpecialDateThenStart(a: ScheduleSpecialDto, b: ScheduleSpecialDto): number {
  return a.onDate === b.onDate ? a.startMinute - b.startMinute : a.onDate.localeCompare(b.onDate);
}

const FIELD_LABEL_CLASSES = "text-[0.78rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES = "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink";

/**
 * The Schedule page's dated-specials section (SPEC F120.3, STORY-317, PLAN T259) — a date/span/
 * persona/show/envelope form plus a plain list of upcoming rows, deliberately NOT a second paint
 * grid (SPEC F120.3's own instruction): one calendar date at a time, no drag, no cells. Collapsed by
 * default (house disclosure idiom, mirrors `ColumnsToggle`'s trigger-button posture) — this is a rare-
 * use tail below the grid the rest of this page exists for, not a control an operator needs open on
 * every visit.
 *
 * <b>THE RESOLVER DOES NOT CONSUME THIS YET.</b> Mirrors `SpecialsController`'s own class remarks
 * (PLAN T259, the T118→T120 pattern): a special created here writes/reads the store only — nothing on
 * the production feeder path shadows the weekly grid with it until PLAN T260 wires that consumption
 * live. This form does not claim otherwise anywhere in its copy.
 *
 * <b>Edit = delete + recreate (SPEC F120.3's own "acceptable for v1" allowance).</b> There is no
 * `PATCH /api/schedule/specials/{id}` — clicking Edit pre-fills the form from the selected row, and
 * submitting it first DELETEs the original row, then POSTs the edited one. This is honestly NOT
 * atomic: if the DELETE succeeds but the POST is then rejected (e.g. the edited span now overlaps a
 * DIFFERENT special, or a network drop between the two calls), the original row is already gone and
 * the edit did not land — the toast/list both reflect that truthfully (the row disappears, the
 * rejection is reported) rather than silently pretending the edit round-tripped. An operator who hits
 * this re-creates the special from scratch; nothing in this epic's scope needs the stronger atomic
 * guarantee a real PATCH would give.
 *
 * <b>Rejections surface as toasts (design-aesthetic: "mutation outcomes as toasts"), never a second,
 * bespoke banner.</b> Mirrors `ShowsClient`/`ScheduleShowPicker`'s own posture (SPEC F120.1's own
 * "surface honestly" instruction, the T244/T245 precedents this task cites): the server's own
 * `detail` — the EXCLUDE overlap's date+span, the past-date/unknown-persona/unknown-show wording — is
 * read verbatim via `readErrorMessage`, never reshaped or genericized.
 */
export function SpecialsForm({ personas, shows, specials }: SpecialsFormProps): ReactNode {
  const [isOpen, setIsOpen] = useState(false);
  const [rows, setRows] = useState<ScheduleSpecialDto[]>(specials.kind === "loaded" ? specials.specials : []);
  const [mode, setMode] = useState<FormMode>({ kind: "create" });
  const [form, setForm] = useState<FormValues>(EMPTY_FORM);
  const [isSaving, setIsSaving] = useState(false);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const confirm = useConfirm();
  const sectionId = useId();

  const isEditing = mode.kind === "edit";
  const isSpanInvalid = form.endMinute <= form.startMinute;
  const showsById = shows.kind === "loaded" ? new Map(shows.shows.map((show) => [show.id, show.name])) : new Map();
  const personasById = new Map(personas.map((persona) => [persona.id, persona.name]));

  function startEdit(special: ScheduleSpecialDto): void {
    setMode({ kind: "edit", id: special.id });
    setForm(formValuesFromSpecial(special));
  }

  function cancelEdit(): void {
    setMode({ kind: "create" });
    setForm(EMPTY_FORM);
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    if (form.onDate === "" || isSpanInvalid) return;

    setIsSaving(true);
    const editingId = mode.kind === "edit" ? mode.id : null;

    // Edit = delete-then-post (see this component's own class remarks for the honest non-atomic
    // tradeoff). The original row is removed from local state the moment the DELETE succeeds — the
    // list must never keep showing a row this form is about to replace while the POST is in flight.
    if (editingId !== null) {
      try {
        const deleteResp = await fetch(`/api/schedule/specials/${editingId}`, { method: "DELETE" });
        if (deleteResp.status !== 204 && deleteResp.status !== 200) {
          toast.error(await readErrorMessage(deleteResp));
          setIsSaving(false);
          return;
        }
      } catch {
        toast.error("Network error — check your connection");
        setIsSaving(false);
        return;
      }
      setRows((prev) => prev.filter((row) => row.id !== editingId));
    }

    try {
      const resp = await fetch("/api/schedule/specials", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(requestBodyFrom(form)),
      });

      if (resp.status === 201) {
        const created = (await resp.json()) as ScheduleSpecialDto;
        setRows((prev) => [...prev, created].sort(bySpecialDateThenStart));
        toast.success(editingId === null ? "Special created." : "Special updated.");
        setMode({ kind: "create" });
        setForm(EMPTY_FORM);
        setIsSaving(false);
        return;
      }

      toast.error(await readErrorMessage(resp));
    } catch {
      toast.error("Network error — check your connection");
    }
    setIsSaving(false);
  }

  async function handleDelete(special: ScheduleSpecialDto): Promise<void> {
    const confirmed = await confirm({
      title: "Delete special",
      consequence: `Delete the special for ${special.onDate} (${formatMinuteRange(special.startMinute, special.endMinute)})? This cannot be undone.`,
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!confirmed) return;

    setDeletingId(special.id);
    try {
      const resp = await fetch(`/api/schedule/specials/${special.id}`, { method: "DELETE" });
      if (resp.status === 204 || resp.status === 200) {
        setRows((prev) => prev.filter((row) => row.id !== special.id));
        if (isEditing && mode.id === special.id) cancelEdit();
        toast.success("Special deleted.");
      } else {
        toast.error(await readErrorMessage(resp));
      }
    } catch {
      toast.error("Network error — check your connection");
    }
    setDeletingId(null);
  }

  return (
    <section aria-label="Specials" className="rounded-[6px] border border-line bg-surface p-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="font-display text-[1.1rem] text-ink">Specials</h2>
          <p className="mt-1 text-[0.82rem] text-mute">
            One-off dated overrides for a single calendar date — not a second paint grid.
          </p>
        </div>
        <Button
          type="button"
          variant="secondary"
          aria-expanded={isOpen}
          aria-controls={sectionId}
          onClick={() => setIsOpen((prev) => !prev)}
        >
          {isOpen ? "Hide" : "Show"}
        </Button>
      </div>

      {isOpen && (
        <div id={sectionId} className="mt-5 flex flex-col gap-6">
          <form
            aria-label={isEditing ? "Edit special" : "Create special"}
            onSubmit={(e) => {
              void handleSubmit(e);
            }}
            className="flex flex-col gap-4"
          >
            <div className="flex flex-col gap-1.5">
              <label htmlFor="special-date" className={FIELD_LABEL_CLASSES}>
                Date
              </label>
              <input
                id="special-date"
                type="date"
                required
                value={form.onDate}
                onChange={(e) => {
                  const onDate = e.currentTarget.value;
                  setForm((prev) => ({ ...prev, onDate }));
                }}
                disabled={isSaving}
                className={FIELD_INPUT_CLASSES}
              />
            </div>

            <div className="flex flex-wrap gap-3">
              <div className="flex flex-1 flex-col gap-1.5">
                <label htmlFor="special-start" className={FIELD_LABEL_CLASSES}>
                  Start
                </label>
                <select
                  id="special-start"
                  value={form.startMinute}
                  onChange={(e) => {
                    const startMinute = Number(e.currentTarget.value);
                    setForm((prev) => ({ ...prev, startMinute }));
                  }}
                  disabled={isSaving}
                  className={FIELD_INPUT_CLASSES}
                >
                  {START_MINUTE_OPTIONS.map((minute) => (
                    <option key={minute} value={minute}>
                      {formatHalfHourLabel(minute / MINUTES_PER_HALF_HOUR)}
                    </option>
                  ))}
                </select>
              </div>
              <div className="flex flex-1 flex-col gap-1.5">
                <label htmlFor="special-end" className={FIELD_LABEL_CLASSES}>
                  End
                </label>
                <select
                  id="special-end"
                  value={form.endMinute}
                  onChange={(e) => {
                    const endMinute = Number(e.currentTarget.value);
                    setForm((prev) => ({ ...prev, endMinute }));
                  }}
                  disabled={isSaving}
                  className={FIELD_INPUT_CLASSES}
                >
                  {END_MINUTE_OPTIONS.map((minute) => (
                    <option key={minute} value={minute}>
                      {formatHalfHourLabel(minute / MINUTES_PER_HALF_HOUR)}
                    </option>
                  ))}
                </select>
              </div>
            </div>
            {isSpanInvalid && <p className="text-[0.78rem] text-danger">End must be after start.</p>}

            <div className="flex flex-wrap gap-3">
              <div className="flex flex-1 flex-col gap-1.5">
                <label htmlFor="special-persona" className={FIELD_LABEL_CLASSES}>
                  Persona
                </label>
                <select
                  id="special-persona"
                  value={form.personaId}
                  onChange={(e) => {
                    const personaId = e.currentTarget.value;
                    setForm((prev) => ({ ...prev, personaId }));
                  }}
                  disabled={isSaving}
                  className={FIELD_INPUT_CLASSES}
                >
                  <option value="">Music only</option>
                  {personas.map((persona) => (
                    <option key={persona.id} value={persona.id}>
                      {persona.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="flex flex-1 flex-col gap-1.5">
                <label htmlFor="special-show" className={FIELD_LABEL_CLASSES}>
                  Show
                </label>
                <select
                  id="special-show"
                  value={form.showId}
                  disabled={isSaving || shows.kind === "error"}
                  onChange={(e) => {
                    const showId = e.currentTarget.value;
                    setForm((prev) => ({ ...prev, showId }));
                  }}
                  className={FIELD_INPUT_CLASSES}
                >
                  <option value="">No show</option>
                  {shows.kind === "loaded" &&
                    shows.shows.map((show) => (
                      <option key={show.id} value={show.id}>
                        {show.name}
                      </option>
                    ))}
                </select>
                {shows.kind === "error" && (
                  <p role="alert" className="text-[0.78rem] text-danger">
                    Show list unavailable — reload the page to assign a show.
                  </p>
                )}
              </div>
            </div>

            <div className="flex flex-col gap-1.5">
              <label htmlFor="special-genres" className={FIELD_LABEL_CLASSES}>
                Genres (comma-separated, blank = station default)
              </label>
              <input
                id="special-genres"
                type="text"
                value={form.genresText}
                onChange={(e) => {
                  const genresText = e.currentTarget.value;
                  setForm((prev) => ({ ...prev, genresText }));
                }}
                disabled={isSaving}
                className={FIELD_INPUT_CLASSES}
              />
            </div>

            <div className="flex gap-3">
              <div className="flex flex-1 flex-col gap-1.5">
                <label htmlFor="special-energy-min" className={FIELD_LABEL_CLASSES}>
                  Energy min
                </label>
                <input
                  id="special-energy-min"
                  type="number"
                  step="0.01"
                  min="0"
                  max="1"
                  value={form.energyMinText}
                  onChange={(e) => {
                    const energyMinText = e.currentTarget.value;
                    setForm((prev) => ({ ...prev, energyMinText }));
                  }}
                  disabled={isSaving}
                  className={FIELD_INPUT_CLASSES}
                />
              </div>
              <div className="flex flex-1 flex-col gap-1.5">
                <label htmlFor="special-energy-max" className={FIELD_LABEL_CLASSES}>
                  Energy max
                </label>
                <input
                  id="special-energy-max"
                  type="number"
                  step="0.01"
                  min="0"
                  max="1"
                  value={form.energyMaxText}
                  onChange={(e) => {
                    const energyMaxText = e.currentTarget.value;
                    setForm((prev) => ({ ...prev, energyMaxText }));
                  }}
                  disabled={isSaving}
                  className={FIELD_INPUT_CLASSES}
                />
              </div>
            </div>

            <div className="flex flex-wrap gap-2">
              <Button type="submit" disabled={isSaving || form.onDate === "" || isSpanInvalid}>
                {isSaving ? "Saving…" : isEditing ? "Save changes" : "Create special"}
              </Button>
              {isEditing && (
                <Button type="button" variant="secondary" onClick={cancelEdit} disabled={isSaving}>
                  Cancel
                </Button>
              )}
            </div>
          </form>

          <div>
            <h3 className="text-[0.85rem] font-semibold text-ink">Upcoming specials</h3>

            {specials.kind === "error" && (
              <p role="alert" className="mt-2 text-[0.78rem] text-danger">
                Unable to load the specials list — reload the page to see current rows.
              </p>
            )}

            {rows.length === 0 ? (
              <EmptyState
                className="mt-3"
                title="No specials scheduled"
                reason="Create one above to shadow the weekly grid for a single date."
              />
            ) : (
              <ul aria-label="Special list" className="mt-3 flex flex-col gap-2">
                {rows.map((special) => (
                  <li
                    key={special.id}
                    className="flex flex-wrap items-center justify-between gap-3 rounded-[6px] border border-line bg-surface-2 px-3 py-2"
                  >
                    <div className="text-[0.85rem] text-ink">
                      <span className="font-semibold tabular-nums">{special.onDate}</span>{" "}
                      <span className="tabular-nums text-mute">
                        {formatMinuteRange(special.startMinute, special.endMinute)}
                      </span>{" "}
                      <span className="text-mute">
                        · {special.personaId === null ? "Music only" : (personasById.get(special.personaId) ?? "Unknown persona")}
                        {special.showId !== null && ` · ${showsById.get(special.showId) ?? "Unknown show"}`}
                      </span>
                    </div>
                    <div className="flex gap-2">
                      <Button
                        type="button"
                        variant="secondary"
                        aria-label={`Edit special ${special.onDate}`}
                        onClick={() => startEdit(special)}
                      >
                        Edit
                      </Button>
                      <Button
                        type="button"
                        variant="secondary"
                        aria-label={`Delete special ${special.onDate}`}
                        disabled={deletingId === special.id}
                        onClick={() => {
                          void handleDelete(special);
                        }}
                      >
                        {deletingId === special.id ? "Deleting…" : "Delete"}
                      </Button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </section>
  );
}
