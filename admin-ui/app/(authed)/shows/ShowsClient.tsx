"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { EmptyState } from "@/components/ui/empty-state";
import { toast } from "@/components/ui/toast";
import { formatDateStamp } from "@/lib/format-clock";
import { readErrorMessage } from "@/lib/problem-details";
import type { ScopedImagingRowDto, ShowDeleteResponseDto, ShowDto } from "./types";

export interface ShowsClientProps {
  /** Every show row, from `GET /api/shows` (SPEC F115.1). */
  initialShows: ShowDto[];
  /** Test-only injection point for the provenance line's `formatDateStamp` call; production omits
   * this and gets the browser's local zone — the same PersonasClient/WardrobeClient idiom, not a
   * bespoke one. */
  timeZone?: string;
}

interface FormValues {
  name: string;
  tagline: string;
  flavor: string;
}

const EMPTY_FORM: FormValues = { name: "", tagline: "", flavor: "" };

/** `edit` carries the slug the form's `PATCH` targets, frozen at `startEdit` time — a show's slug
 * re-derives from its name on every authored edit (`ShowRepository.UpdateAsync`), so the field the
 * operator is mid-typing a new name into must never become the address the request is sent to;
 * `id` is what re-splices the row back into `shows` once the PATCH answers with whatever slug the
 * rename actually produced. */
type FormMode = { kind: "create" } | { kind: "edit"; id: number; slug: string };

/** Body accepted by `POST/PATCH /api/shows` (mirrors `GenWave.Host.Api.ShowRequest`). `tagline`/
 * `flavor` travel as plain strings, blank included — the store's own `NullIfBlank` (`ShowRepository`)
 * persists a blank/whitespace-only value as `NULL`, so this form never needs to decide that itself. */
interface ShowRequestBody {
  name: string;
  tagline: string;
  flavor: string;
}

function requestBodyFrom(form: FormValues): ShowRequestBody {
  return { name: form.name.trim(), tagline: form.tagline, flavor: form.flavor };
}

/** SPEC F115.1's field-length budgets, mirrored from `GenWave.Core.Domain.ShowBudgets` — THAT file
 * is the source of truth (the API enforces it at 1x regardless of what this form sends); these three
 * constants exist only so the form's own `maxLength` attributes and char counters can stop an
 * over-budget round trip before it ever reaches the wire (SPEC F115.1's own "UI maxlength prevents
 * the round-trip" framing). */
const NAME_MAX_CHARS = 60;
const TAGLINE_MAX_CHARS = 120;
const FLAVOR_MAX_CHARS = 400;

/** The one label a show's own delete refusal needs to surface (SPEC F115.4) — the server's `detail`
 * already IS the block-naming prose (`"<slug>" is still scheduled and cannot be deleted: Mon
 * 09:00–12:00, …`), so this reads it verbatim via `readErrorMessage` rather than re-parsing it into a
 * bespoke shape the way `WardrobeClient`'s `formatReferencedThemesMessage` does for its own
 * differently-shaped 409 — that reshape earns its keep there because the theme names sit inside a
 * longer sentence; here the whole sentence already reads as the plain-words refusal. */
function deleteConsequence(show: ShowDto): string {
  return `Delete "${show.name}"? This cannot be undone.`;
}

/** The names/ids a successful delete's 200 body unscoped (SPEC F115.4) — folded into the success
 * toast so an operator sees what else the delete touched, not just that it succeeded. A row with no
 * title falls back to its id: `ScopedImagingRowDto.title` is `null` only on the
 * should-never-happen chance `library.media` carries none for that row. */
function unscopedImagingNames(rows: readonly ScopedImagingRowDto[]): string[] {
  return rows.map((row) => row.title ?? `media #${row.mediaId}`);
}

/** Provenance line (SPEC F115.1, F90.7's own three-field pattern) — "Imported · &lt;source&gt; ·
 * &lt;date&gt;" for an imported show, nothing for one authored in place. `importedFrom` renders
 * VERBATIM, the same provenance-not-decoration rule `PersonasClient`'s `ProvenanceBadge` and
 * `WardrobeClient`'s `ProvenanceChip` already follow. */
function ProvenanceLine({
  importedFrom,
  importedAt,
  timeZone,
}: {
  importedFrom: string;
  importedAt: string;
  timeZone?: string;
}): ReactNode {
  return <Chip>{`Imported · ${importedFrom} · ${formatDateStamp(importedAt, { timeZone })}`}</Chip>;
}

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";
/** Tabular numerals on the char-counter digits (design-aesthetic skill's numbers rule) — the count
 * ticks up/down as the operator types, so a proportional digit width would visibly jitter. */
const CHAR_COUNTER_CLASSES = "text-[0.72rem] tabular-nums text-mute";

/**
 * The Shows page's client half (SPEC F119.1, STORY-312, PLAN T244): list/create/edit/delete over
 * `/api/shows`. Mirrors `PersonasClient`'s pre-export-first CRUD shape (a single form that toggles
 * create/edit, a list below it, local `shows` state spliced on every mutation response — every
 * mutation here returns a full row, so unlike `PersonasClient`'s import panel there is never a
 * reason to re-fetch the whole list) rather than `WardrobeClient`/`UninstallPackButton`'s
 * server-refresh-on-mutate split: that pair exists because Wardrobe is read-only except for one
 * per-pack action; this page authors every field a show has.
 *
 * <b>No export-first gate (PLAN T244, "your call" — SPEC gives none).</b> `PersonasClient`'s Fire
 * flow (SPEC F94.2) forces an export or an explicit skip before Delete unlocks, because a persona
 * card carries taste history and narrative an operator could lose for good. A show carries three
 * short authored fields (name/tagline/flavor) with no comparable export format anywhere in this
 * codebase (`PersonaExportLink`'s card JSON has no show analogue) and no learned state at all — the
 * plain `useConfirm()` guard `UninstallPackButton` uses for font packs is the proportionate one here
 * too: state the consequence, confirm, delete.
 *
 * <b>Guarded delete (SPEC F115.4).</b> A 409 means `station.segment_schedule` still names this show
 * in ≥1 block; the server's own `detail` already names them (`ShowsController.ReferencedProblem`),
 * so this reads it straight through `readErrorMessage` with no reshape (see `deleteConsequence`'s own
 * remarks for why that differs from `WardrobeClient`'s theme-name reshape). A success that unscoped
 * ≥1 show-scoped imaging row (F117.1, no FK — never what BLOCKS the delete) folds those names into
 * the success toast rather than a second dialog; nothing here re-fetches the imaging/catalog surface,
 * since this page never lists imaging rows in the first place.
 *
 * <b>Imported-provenance gate (SPEC F115.5).</b> An imported show's `Edit` button is left enabled —
 * unlike `PersonasClient`'s Scheduled-row delete-button omission (a certain 409, so no button is
 * offered at all), an imported show's fields are frequently exactly what an operator DOES want to
 * revise even though the write will 409; the refusal toast (`readErrorMessage`'s own detail, e.g.
 * `"retro-nights" was imported … and cannot be edited as an authored show`) names the reason plainly
 * rather than this page silently withholding the control (PLAN T244, "your call" — no F115.x AC
 * requires either posture, and SPEC F119.3 rules out any anticipatory nudge/badge either way).
 *
 * <b>F119.3 — coverage stays neutral.</b> Nothing on this page inspects `station.segment_schedule`
 * for which blocks are unnamed, so there is no signal here TO badge or nudge with — the neutrality is
 * structural, not a withheld feature.
 */
export function ShowsClient({ initialShows, timeZone }: ShowsClientProps): ReactNode {
  const [shows, setShows] = useState<ShowDto[]>(initialShows);
  const [mode, setMode] = useState<FormMode>({ kind: "create" });
  const [form, setForm] = useState<FormValues>(EMPTY_FORM);
  const [isSaving, setIsSaving] = useState(false);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const confirm = useConfirm();

  const isNameBlank = form.name.trim() === "";
  const isEditing = mode.kind === "edit";

  function startEdit(show: ShowDto): void {
    setMode({ kind: "edit", id: show.id, slug: show.slug });
    setForm({ name: show.name, tagline: show.tagline ?? "", flavor: show.flavor ?? "" });
  }

  function cancelEdit(): void {
    setMode({ kind: "create" });
    setForm(EMPTY_FORM);
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    if (isNameBlank) return;

    setIsSaving(true);
    const body = requestBodyFrom(form);

    try {
      const resp =
        mode.kind === "create"
          ? await fetch("/api/shows", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify(body),
            })
          : await fetch(`/api/shows/${encodeURIComponent(mode.slug)}`, {
              method: "PATCH",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify(body),
            });

      if (resp.status === 201 || resp.status === 200) {
        const saved = (await resp.json()) as ShowDto;
        setShows((prev) => {
          const next =
            mode.kind === "create" ? [...prev, saved] : prev.map((s) => (s.id === saved.id ? saved : s));
          return [...next].sort((a, b) => a.name.localeCompare(b.name));
        });
        toast.success(mode.kind === "create" ? `"${saved.name}" created.` : `"${saved.name}" updated.`);
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

  async function handleDelete(show: ShowDto): Promise<void> {
    const confirmed = await confirm({
      title: "Delete show",
      consequence: deleteConsequence(show),
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!confirmed) return;

    setDeletingId(show.id);
    try {
      const resp = await fetch(`/api/shows/${encodeURIComponent(show.slug)}`, { method: "DELETE" });

      if (resp.status === 204 || resp.status === 200) {
        setShows((prev) => prev.filter((s) => s.id !== show.id));
        if (isEditing && mode.id === show.id) cancelEdit();

        if (resp.status === 200) {
          const unscoped = (await resp.json()) as ShowDeleteResponseDto;
          const names = unscopedImagingNames(unscoped.unscopedImaging);
          toast.success(
            names.length > 0
              ? `"${show.name}" deleted. Unscoped imaging: ${names.join(", ")}.`
              : `"${show.name}" deleted.`
          );
        } else {
          toast.success(`"${show.name}" deleted.`);
        }
      } else {
        toast.error(await readErrorMessage(resp));
      }
    } catch {
      toast.error("Network error — check your connection");
    }
    setDeletingId(null);
  }

  return (
    <div className="flex flex-col gap-6">
      <section
        aria-label={isEditing ? "Edit show" : "Create show"}
        className="rounded-[6px] border border-line bg-surface p-5"
      >
        <h2 className="font-display text-[1.1rem] text-ink">{isEditing ? "Edit show" : "New show"}</h2>

        <form
          onSubmit={(e) => {
            void handleSubmit(e);
          }}
          className="mt-4 flex flex-col gap-4"
        >
          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between">
              <label htmlFor="show-name" className={FIELD_LABEL_CLASSES}>
                Name
              </label>
              <span className={CHAR_COUNTER_CLASSES}>
                {form.name.length} / {NAME_MAX_CHARS}
              </span>
            </div>
            <input
              id="show-name"
              type="text"
              value={form.name}
              maxLength={NAME_MAX_CHARS}
              onChange={(e) => {
                const name = e.currentTarget.value;
                setForm((prev) => ({ ...prev, name }));
              }}
              disabled={isSaving}
              className={FIELD_INPUT_CLASSES}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between">
              <label htmlFor="show-tagline" className={FIELD_LABEL_CLASSES}>
                Tagline
              </label>
              <span className={CHAR_COUNTER_CLASSES}>
                {form.tagline.length} / {TAGLINE_MAX_CHARS}
              </span>
            </div>
            <input
              id="show-tagline"
              type="text"
              value={form.tagline}
              maxLength={TAGLINE_MAX_CHARS}
              onChange={(e) => {
                const tagline = e.currentTarget.value;
                setForm((prev) => ({ ...prev, tagline }));
              }}
              disabled={isSaving}
              className={FIELD_INPUT_CLASSES}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-baseline justify-between">
              <label htmlFor="show-flavor" className={FIELD_LABEL_CLASSES}>
                Flavor
              </label>
              <span className={CHAR_COUNTER_CLASSES}>
                {form.flavor.length} / {FLAVOR_MAX_CHARS}
              </span>
            </div>
            <textarea
              id="show-flavor"
              rows={4}
              value={form.flavor}
              maxLength={FLAVOR_MAX_CHARS}
              onChange={(e) => {
                const flavor = e.currentTarget.value;
                setForm((prev) => ({ ...prev, flavor }));
              }}
              disabled={isSaving}
              className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
            />
          </div>

          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={isSaving || isNameBlank}>
              {isSaving ? "Saving…" : isEditing ? "Save changes" : "Create show"}
            </Button>
            {isEditing && (
              <Button type="button" variant="secondary" onClick={cancelEdit} disabled={isSaving}>
                Cancel
              </Button>
            )}
          </div>
        </form>
      </section>

      <section aria-label="Shows">
        <h2 className="font-display text-[1.1rem] text-ink">Shows</h2>

        {shows.length === 0 ? (
          <EmptyState
            className="mt-4"
            title="No shows yet"
            reason="Create the first show above to give a schedule block a name."
            cta={{ label: "Start writing", onClick: () => document.getElementById("show-name")?.focus() }}
          />
        ) : (
          <ul aria-label="Show list" className="mt-4 flex flex-col gap-3">
            {shows.map((show) => (
              <li key={show.id} className="rounded-[6px] border border-line bg-surface p-4">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 data-testid={`show-name-${show.name}`} className="font-display text-[1.1rem] text-ink">
                      {show.name}
                    </h3>
                    {show.importedFrom !== null && show.importedAt !== null && (
                      <ProvenanceLine
                        importedFrom={show.importedFrom}
                        importedAt={show.importedAt}
                        timeZone={timeZone}
                      />
                    )}
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <Button
                      type="button"
                      variant="secondary"
                      aria-label={`Edit ${show.name}`}
                      onClick={() => startEdit(show)}
                    >
                      Edit
                    </Button>
                    <Button
                      type="button"
                      variant="secondary"
                      aria-label={`Delete ${show.name}`}
                      disabled={deletingId === show.id}
                      onClick={() => {
                        void handleDelete(show);
                      }}
                    >
                      {deletingId === show.id ? "Deleting…" : "Delete"}
                    </Button>
                  </div>
                </div>

                {show.tagline !== null && show.tagline !== "" && (
                  <p className="mt-2 text-[0.85rem] text-ink">{show.tagline}</p>
                )}
                {show.flavor !== null && show.flavor !== "" && (
                  <p className="mt-2 text-[0.78rem] text-mute">{show.flavor}</p>
                )}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
