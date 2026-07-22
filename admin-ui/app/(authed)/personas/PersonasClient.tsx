"use client";

import { useRef, useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { EmptyState } from "@/components/ui/empty-state";
import { toast } from "@/components/ui/toast";
import { useVoiceList } from "@/lib/use-voice-list";
import { VoiceControl } from "../safe-content/VoiceControl";
import { PersonaExportLink } from "./PersonaExportLink";
import { PersonaImportPanel } from "./PersonaImportPanel";
import { PersonaPreview } from "./PersonaPreview";
import { readErrorMessage } from "./persona-http";
import { usePersonaVoiceWarning } from "./use-persona-voice-warning";
import type { PersonaDto } from "./types";

export interface PersonasClientProps {
  /** Every persona row, from GET /api/personas (SPEC F35.4). */
  initialPersonas: PersonaDto[];
  /** `Station:Persona:ActiveId` resolved server-side from GET /api/settings; `0` = none. */
  initialActiveId: number;
}

/** The one F19 allowlist key the activate/deactivate control writes (SPEC F35.2). */
const ACTIVE_ID_KEY = "Station:Persona:ActiveId";

/** Shape of a `PersonaRequest` body accepted by POST/PATCH /api/personas (SPEC F35.4). */
interface PersonaRequestBody {
  name: string;
  backstory: string;
  style: string;
  voice?: string;
}

type FormMode = { kind: "create" } | { kind: "edit"; id: number };

interface FormValues {
  name: string;
  backstory: string;
  style: string;
  voice: string;
}

const EMPTY_FORM: FormValues = { name: "", backstory: "", style: "", voice: "" };

const STYLE_SUMMARY_MAX = 60;

/** Truncates the style field for the list column — "" reads as "No style set" rather than a
 * blank cell, so an unstyled persona doesn't look like a loading glitch. */
function summarizeStyle(style: string): string {
  const trimmed = style.trim();
  if (trimmed === "") return "No style set";
  return trimmed.length > STYLE_SUMMARY_MAX ? `${trimmed.slice(0, STYLE_SUMMARY_MAX - 1)}…` : trimmed;
}

/** `""` is the station-default sentinel (SPEC F35.1) — shown as "Station default" everywhere a
 * persona's voice is displayed, matching the VoiceControl's own first option. */
function displayVoice(voice: string): string {
  return voice.trim() === "" ? "Station default" : voice;
}

function requestBodyFrom(form: FormValues): PersonaRequestBody {
  const body: PersonaRequestBody = {
    name: form.name.trim(),
    backstory: form.backstory,
    style: form.style,
  };
  if (form.voice.trim() !== "") body.voice = form.voice;
  return body;
}

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";
const HEADER_CELL = "py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";

/**
 * The Personas page's client half (SPEC F35.7, STORY-126): list/create/edit/delete over
 * `/api/personas`, an activate/deactivate control that writes `Station:Persona:ActiveId` through
 * the shipped `/api/settings` surface (SPEC F35.2), and a preview action (`PersonaPreview`) both
 * per row and for the form's in-progress draft. Personas carry no `If-Match` (F35.4's documented
 * single-writer deviation — see `PersonaController`), so writes here go straight through `fetch`
 * rather than the ETag-bearing `useRowPatch` hook; failures toast per F31.3, mirroring
 * SafeContentClient/LibrariesTab's detail-first ProblemDetails reader.
 *
 * PLAN T68 (STORY-209/210, SPEC F79) adds three more surfaces: `PersonaExportLink` per row
 * (a plain download anchor, F79.1); `PersonaImportPanel` (file → preview → confirm → import,
 * F79.4–F79.6) whose success refreshes this list via `refreshPersonas` rather than splicing a
 * partial row in (the import response carries no full `PersonaDto`); and the F79.4/F79.5
 * import-warning banner in the edit form, derived on read by `usePersonaVoiceWarning` and linked
 * to the `VoiceControl` picker right below it.
 */
export function PersonasClient({ initialPersonas, initialActiveId }: PersonasClientProps): ReactNode {
  const confirm = useConfirm();
  const [personas, setPersonas] = useState<PersonaDto[]>(initialPersonas);
  const [activeId, setActiveId] = useState<number>(initialActiveId);

  const [mode, setMode] = useState<FormMode>({ kind: "create" });
  const [form, setForm] = useState<FormValues>(EMPTY_FORM);
  const [isSaving, setIsSaving] = useState(false);
  const nameFieldRef = useRef<HTMLInputElement | null>(null);

  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [activatingId, setActivatingId] = useState<number | null>(null);

  // F79.4/F79.5 import-warning derivation: the live voice list (shared implementation with
  // VoiceControl's own dropdown, see useVoiceList's remarks) compared against the edit-form
  // candidate's export card. null candidate outside the edit form clears any prior warning.
  const voiceList = useVoiceList();
  const editingPersona = mode.kind === "edit" ? personas.find((p) => p.id === mode.id) ?? null : null;
  const voiceWarning = usePersonaVoiceWarning(editingPersona, voiceList);

  const isNameBlank = form.name.trim() === "";

  /** Re-reads the full persona list (SPEC F79.3's import upsert may create or update a row this
   * component has no local copy of yet) — called after a successful import; best-effort, since
   * the import itself already succeeded and toasted (F28.9). */
  async function refreshPersonas(): Promise<void> {
    try {
      const resp = await fetch("/api/personas");
      if (resp.ok) {
        setPersonas((await resp.json()) as PersonaDto[]);
      }
    } catch {
      // stale list until the next action — not a lost write.
    }
  }

  function startEdit(persona: PersonaDto): void {
    setMode({ kind: "edit", id: persona.id });
    setForm({
      name: persona.name,
      backstory: persona.backstory,
      style: persona.style,
      voice: persona.voice,
    });
  }

  function cancelEdit(): void {
    setMode({ kind: "create" });
    setForm(EMPTY_FORM);
  }

  function focusNameField(): void {
    nameFieldRef.current?.focus();
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    if (isNameBlank) return;

    setIsSaving(true);
    const body = requestBodyFrom(form);

    try {
      const resp =
        mode.kind === "create"
          ? await fetch("/api/personas", {
              method: "POST",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify(body),
            })
          : await fetch(`/api/personas/${mode.id}`, {
              method: "PATCH",
              headers: { "Content-Type": "application/json" },
              body: JSON.stringify(body),
            });

      if (resp.status === 201 || resp.status === 200) {
        const saved = (await resp.json()) as PersonaDto;
        setPersonas((prev) => {
          const next =
            mode.kind === "create"
              ? [...prev, saved]
              : prev.map((p) => (p.id === saved.id ? saved : p));
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

  async function handleDelete(persona: PersonaDto): Promise<void> {
    const isActive = persona.id === activeId;
    const ok = await confirm({
      title: "Delete persona",
      consequence: isActive
        ? `Delete "${persona.name}"? This deactivates the DJ — blurbs continue in the neutral house style.`
        : `Delete "${persona.name}"? This cannot be undone.`,
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!ok) return;

    setDeletingId(persona.id);
    try {
      const resp = await fetch(`/api/personas/${persona.id}`, { method: "DELETE" });
      if (resp.status === 204) {
        setPersonas((prev) => prev.filter((p) => p.id !== persona.id));
        // The API clears Station:Persona:ActiveId server-side in the same request when the
        // deleted persona was active (SPEC F35.5) — reflect that locally so the badge disappears
        // without a round-trip refetch.
        if (isActive) setActiveId(0);
        if (mode.kind === "edit" && mode.id === persona.id) cancelEdit();
        toast.success(`"${persona.name}" deleted.`);
      } else {
        toast.error(await readErrorMessage(resp));
      }
    } catch {
      toast.error("Network error — check your connection");
    }
    setDeletingId(null);
  }

  async function handleActivate(persona: PersonaDto): Promise<void> {
    const nextId = activeId === persona.id ? 0 : persona.id;

    setActivatingId(persona.id);
    try {
      const resp = await fetch("/api/settings", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify([{ key: ACTIVE_ID_KEY, value: String(nextId) }]),
      });

      if (resp.ok) {
        setActiveId(nextId);
        toast.success(
          nextId === 0
            ? "Deactivated — blurbs continue in the neutral house style."
            : `"${persona.name}" is now the active DJ.`
        );
      } else {
        toast.error(await readErrorMessage(resp));
      }
    } catch {
      toast.error("Network error — check your connection");
    }
    setActivatingId(null);
  }

  const isSectionEditing = mode.kind === "edit";

  return (
    <div className="flex flex-col gap-6">
      <section
        aria-label={isSectionEditing ? "Edit persona" : "Create persona"}
        className="rounded-[6px] border border-line bg-surface p-5"
      >
        <h2 className="font-display text-[1.1rem] text-ink">
          {isSectionEditing ? "Edit persona" : "New persona"}
        </h2>

        <form
          onSubmit={(e) => {
            void handleSubmit(e);
          }}
          className="mt-4 flex flex-col gap-4"
        >
          <div className="flex flex-col gap-1.5">
            <label htmlFor="persona-name" className={FIELD_LABEL_CLASSES}>
              Name
            </label>
            <input
              id="persona-name"
              ref={nameFieldRef}
              type="text"
              value={form.name}
              onChange={(e) => {
                const name = e.currentTarget.value;
                setForm((prev) => ({ ...prev, name }));
              }}
              disabled={isSaving}
              className={FIELD_INPUT_CLASSES}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label htmlFor="persona-backstory" className={FIELD_LABEL_CLASSES}>
              Backstory
            </label>
            <textarea
              id="persona-backstory"
              rows={3}
              value={form.backstory}
              onChange={(e) => {
                const backstory = e.currentTarget.value;
                setForm((prev) => ({ ...prev, backstory }));
              }}
              disabled={isSaving}
              className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label htmlFor="persona-style" className={FIELD_LABEL_CLASSES}>
              Style
            </label>
            <textarea
              id="persona-style"
              rows={3}
              value={form.style}
              onChange={(e) => {
                const style = e.currentTarget.value;
                setForm((prev) => ({ ...prev, style }));
              }}
              disabled={isSaving}
              className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
            />
          </div>

          {voiceWarning !== null && (
            <p role="alert" className="text-[0.82rem] text-danger">
              Voice &quot;{voiceWarning.voiceId}&quot; from the imported card isn&apos;t available on
              this station — the persona is using the station default.{" "}
              <a href="#persona-voice" className="font-semibold underline">
                Pick a voice below
              </a>
              .
            </p>
          )}

          <VoiceControl
            id="persona-voice"
            value={form.voice}
            onChange={(voice) => setForm((prev) => ({ ...prev, voice }))}
            disabled={isSaving}
          />

          <div className="flex flex-wrap gap-2">
            <Button type="submit" disabled={isSaving || isNameBlank}>
              {isSaving ? "Saving…" : isSectionEditing ? "Save changes" : "Create persona"}
            </Button>
            {isSectionEditing && (
              <Button type="button" variant="secondary" onClick={cancelEdit} disabled={isSaving}>
                Cancel
              </Button>
            )}
          </div>
        </form>

        <div className="mt-4 border-t border-line pt-4">
          <PersonaPreview
            target={{
              kind: "draft",
              name: form.name,
              backstory: form.backstory,
              style: form.style,
              voice: form.voice,
            }}
          />
        </div>
      </section>

      <section aria-label="Import persona" className="rounded-[6px] border border-line bg-surface p-5">
        <h2 className="font-display text-[1.1rem] text-ink">Import a persona</h2>
        <div className="mt-4">
          <PersonaImportPanel
            onImported={() => {
              void refreshPersonas();
            }}
          />
        </div>
      </section>

      <section aria-label="Personas">
        <h2 className="font-display text-[1.1rem] text-ink">Personas</h2>

        {personas.length === 0 ? (
          <EmptyState
            className="mt-4"
            title="No DJ personas yet"
            reason="Create the first persona above to give the station a voice."
            cta={{ label: "Start writing", onClick: focusNameField }}
          />
        ) : (
          // AC2 (SPEC F28.13): scrolls sideways inside this container at 390px — the page body
          // itself never does.
          <div className="mt-4 overflow-x-auto">
            <table className="w-full border-collapse text-[0.85rem]">
              <thead>
                <tr className="border-b-2 border-line text-left">
                  <th scope="col" className={HEADER_CELL}>
                    Name
                  </th>
                  <th scope="col" className={HEADER_CELL}>
                    Style
                  </th>
                  <th scope="col" className={HEADER_CELL}>
                    Voice
                  </th>
                  <th scope="col" className={HEADER_CELL}>
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody>
                {personas.map((persona) => {
                  const isActive = persona.id === activeId;
                  return (
                    <tr key={persona.id} className="border-b border-line last:border-b-0">
                      <td className="py-2 pr-3 text-ink">
                        <span data-testid={`persona-name-${persona.name}`}>{persona.name}</span>
                        {isActive && (
                          <span className="ml-2 inline-flex items-center rounded-[999px] bg-accent px-2 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-ink">
                            Active
                          </span>
                        )}
                      </td>
                      <td className="py-2 pr-3 text-mute">{summarizeStyle(persona.style)}</td>
                      <td className="py-2 pr-3 text-mute">{displayVoice(persona.voice)}</td>
                      <td className="py-2 pr-3">
                        <div className="flex flex-col gap-2">
                          <div className="flex flex-wrap gap-2">
                            <Button
                              type="button"
                              variant="secondary"
                              aria-label={`Edit ${persona.name}`}
                              onClick={() => startEdit(persona)}
                            >
                              Edit
                            </Button>
                            <Button
                              type="button"
                              variant="secondary"
                              aria-label={`${isActive ? "Deactivate" : "Activate"} ${persona.name}`}
                              disabled={activatingId === persona.id}
                              onClick={() => {
                                void handleActivate(persona);
                              }}
                            >
                              {isActive ? "Deactivate" : "Activate"}
                            </Button>
                            <Button
                              type="button"
                              variant="secondary"
                              aria-label={`Delete ${persona.name}`}
                              disabled={deletingId === persona.id}
                              onClick={() => {
                                void handleDelete(persona);
                              }}
                            >
                              Delete
                            </Button>
                            <PersonaExportLink persona={persona} />
                          </div>
                          <PersonaPreview
                            target={{ kind: "saved", personaId: persona.id, voice: persona.voice }}
                          />
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
