"use client";

import { useCallback, useEffect, useState, type KeyboardEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { CELL_INPUT_CLASSES, HEADER_CELL } from "./rule-table-styles";
import { isValidationProblemDetails } from "./settings-types";

/**
 * One row of `GET /api/pronunciations` — mirrors `GenWave.Host.Api.PronunciationRuleDto` (T144,
 * SPEC F97.3, F100.3, STORY-254).
 */
interface PronunciationRuleRow {
  pattern: string;
  word: string;
  ipa: string;
  source: "station" | "persona";
  inEffect: boolean;
  hitCount: number | null;
  reason: string | null;
}

function isPronunciationRuleRow(raw: unknown): raw is PronunciationRuleRow {
  if (typeof raw !== "object" || raw === null) return false;
  const obj = raw as Record<string, unknown>;
  return (
    typeof obj["pattern"] === "string" &&
    typeof obj["word"] === "string" &&
    typeof obj["ipa"] === "string" &&
    (obj["source"] === "station" || obj["source"] === "persona") &&
    typeof obj["inEffect"] === "boolean" &&
    (obj["hitCount"] === null || typeof obj["hitCount"] === "number") &&
    (obj["reason"] === null || typeof obj["reason"] === "string")
  );
}

function isPronunciationRuleRowList(raw: unknown): raw is PronunciationRuleRow[] {
  return Array.isArray(raw) && raw.every(isPronunciationRuleRow);
}

/** `POST`/`PUT /api/pronunciations` body — `word` collapses blank to `null` so an unspecified
 * word defaults to the pattern server-side, the same convention `PronunciationRule.Parse` uses. */
function writeBody(pattern: string, word: string, ipa: string): { pattern: string; word: string | null; ipa: string } {
  const trimmedWord = word.trim();
  return { pattern: pattern.trim(), word: trimmedWord === "" ? null : trimmedWord, ipa };
}

function ruleUrl(pattern: string, word: string): string {
  return `/api/pronunciations?pattern=${encodeURIComponent(pattern)}&word=${encodeURIComponent(word)}`;
}

async function fetchRows(): Promise<PronunciationRuleRow[]> {
  const resp = await fetch("/api/pronunciations", { credentials: "include", cache: "no-store" });
  if (!resp.ok) throw new Error(`GET /api/pronunciations failed: ${resp.status}`);
  const raw: unknown = await resp.json();
  if (!isPronunciationRuleRowList(raw)) throw new Error("GET /api/pronunciations returned an unreadable shape");
  return raw;
}

/** The outcomes a POST/PUT/DELETE against `/api/pronunciations` resolves to — named per the
 * status code so a caller's UI treatment (inline field / row message / toast) reads directly off
 * the kind rather than re-inspecting a status number. */
type WriteOutcome =
  | { kind: "ok" }
  | { kind: "invalid"; fieldErrors: Record<string, string[]> }
  | { kind: "conflict"; message: string }
  | { kind: "stale"; message: string }
  | { kind: "error"; message: string };

/** `readErrorMessage` (`@/lib/problem-details`) is the house `ProblemDetails.detail` reader
 * (T102 review) — reused here for the 409 layer and the generic fallback rather than a
 * per-control copy (PLAN T145 review should-fix); its own default (`Unexpected error (status)`)
 * is exactly what an un-detailed non-400/409/404 status wants too. */
async function interpretWriteFailure(resp: Response): Promise<WriteOutcome> {
  if (resp.status === 400) {
    try {
      const raw: unknown = await resp.json();
      if (isValidationProblemDetails(raw)) return { kind: "invalid", fieldErrors: raw.errors };
    } catch {
      // malformed body — fall through to an empty field-error set
    }
    return { kind: "invalid", fieldErrors: {} };
  }
  if (resp.status === 409) {
    return { kind: "conflict", message: await readErrorMessage(resp) };
  }
  if (resp.status === 404) {
    return {
      kind: "stale",
      message: "This rule no longer exists — it may have changed in another tab. The list has been refreshed.",
    };
  }
  return { kind: "error", message: await readErrorMessage(resp) };
}

async function postRule(pattern: string, word: string, ipa: string): Promise<WriteOutcome> {
  try {
    const resp = await fetch("/api/pronunciations", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(writeBody(pattern, word, ipa)),
    });
    return resp.ok ? { kind: "ok" } : await interpretWriteFailure(resp);
  } catch {
    return { kind: "error", message: "Network error — check your connection." };
  }
}

async function putRule(
  originalPattern: string,
  originalWord: string,
  pattern: string,
  word: string,
  ipa: string
): Promise<WriteOutcome> {
  try {
    const resp = await fetch(ruleUrl(originalPattern, originalWord), {
      method: "PUT",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(writeBody(pattern, word, ipa)),
    });
    return resp.ok ? { kind: "ok" } : await interpretWriteFailure(resp);
  } catch {
    return { kind: "error", message: "Network error — check your connection." };
  }
}

async function deleteRule(pattern: string, word: string): Promise<WriteOutcome> {
  try {
    const resp = await fetch(ruleUrl(pattern, word), { method: "DELETE", credentials: "include" });
    return resp.ok ? { kind: "ok" } : await interpretWriteFailure(resp);
  } catch {
    return { kind: "error", message: "Network error — check your connection." };
  }
}

/** The generic toast copy for any outcome that isn't handled inline — `invalid`/`conflict` are
 * defensive fallbacks here (DELETE never actually returns them), `stale`/`error` carry their own
 * message already. */
function toastMessageFor(outcome: WriteOutcome): string {
  switch (outcome.kind) {
    case "ok":
      return "";
    case "invalid":
      return "This rule was rejected.";
    case "conflict":
      return outcome.message;
    case "stale":
      return outcome.message;
    case "error":
      return outcome.message;
  }
}

type ListState =
  | { kind: "loading" }
  | { kind: "loaded"; rows: PronunciationRuleRow[] }
  | { kind: "error" };

interface EditingDraft {
  /** The row's identity BEFORE this edit — the PUT's `?pattern=&word=` target, and (PLAN T145
   * review F1) the SOURCE half of that identity too. Pattern/word alone is not unique across a
   * shadowed pair — a station row and a persona row can (and, in the shadowed case that motivated
   * this field, DO) share the same (pattern, word) — so matching on pattern/word alone let a
   * persona row's identity collide with the station row actually being edited, rendering the
   * persona row as editable inputs showing the STATION's draft. */
  originalPattern: string;
  originalWord: string;
  originalSource: PronunciationRuleRow["source"];
  pattern: string;
  word: string;
  ipa: string;
}

type EditingState =
  | { kind: "idle" }
  | {
      kind: "editing";
      draft: EditingDraft;
      fieldErrors: Record<string, string[]>;
      rowError: string | null;
      pending: boolean;
    };

function isEditingRow(editing: EditingState, row: PronunciationRuleRow): boolean {
  return (
    editing.kind === "editing" &&
    editing.draft.originalSource === row.source &&
    editing.draft.originalPattern === row.pattern &&
    editing.draft.originalWord === row.word
  );
}

/** The rule's own display identity for row-scoped aria-labels and the delete confirmation (the
 * ShowsClient `${show.name}` idiom, PLAN T145 review F4) — falls back to a neutral phrase for a
 * dead row's blank-pattern identity (T144 review F3) so the label never reads as literally
 * nothing. */
function ruleIdentityLabel(row: PronunciationRuleRow): string {
  return row.pattern !== "" ? `"${row.pattern}"` : "this blank-pattern rule";
}

/** A stable per-row key spanning BOTH halves of a row's real identity (source + pattern/word) —
 * used as the React list key and as the delete-pending guard's address, so a shadowed pair's two
 * rows (same pattern/word, different source) can never be confused for one another (PLAN T145
 * review F1's same underlying lesson, applied to the parts of this file that key on a row). The
 * `\x1F` (Unit Separator) delimiter mirrors `PronunciationsController.HitKey` on the server —
 * a control character no operator-authored pattern/word plausibly contains, so the three parts
 * can never collide across the join the way a printable delimiter theoretically could. */
function ruleIdentityKey(row: PronunciationRuleRow): string {
  return `${row.source}\x1F${row.pattern}\x1F${row.word}`;
}

function deleteConsequence(row: PronunciationRuleRow): string {
  return `Delete the pronunciation rule for ${ruleIdentityLabel(row)}? This cannot be undone.`;
}

const FIELD_ERROR_CLASSES = "text-[0.75rem] text-danger";
const COLUMN_COUNT = 7;

/**
 * Pronunciation rules editor (SPEC F97, F100.3, STORY-254, PLAN T145) — reads and writes
 * `GET/POST/PUT/DELETE /api/pronunciations` directly (T144), never the `Tts:Pronunciations`
 * settings blob. Mounted by the Settings page (`page.tsx`) through `SettingsForm`'s `ttsTabExtra`
 * prop (PLAN T145 review F3), which lands it INSIDE the TTS tabpanel, right after that tab's own
 * section cards — genuinely beside the raw `Tts:Pronunciations` field (kept as-is; it stays a
 * legitimate hand-edit escape hatch, per that field's own documented remarks), not a page-level
 * section below the whole tabbed form.
 *
 * <b>Why not `SETTING_CONTROL_REGISTRY` (the `CorrectionsSettingControl` precedent).</b> The
 * registry's `value`/`onChange` contract stages one opaque string into the page-wide changed-keys
 * PUT batch, but this surface (a) reads a MERGED station∪persona view no single settings key
 * carries, (b) must call the dedicated endpoint for its richer `PronunciationRuleValidator`
 * checks and its atomic (pattern, word) uniqueness guard — a raw blob PUT would bypass both, and
 * (c) needs each add/edit/delete to resolve immediately (its own 201/400/409/404), not deferred
 * behind a page-wide Save. Forcing that shape into `SettingControlProps` would mean either faking
 * a value/onChange round-trip nothing reads, or fighting the batch-save model outright.
 *
 * <b>Why `ttsTabExtra`, not `SettingsForm` importing this component directly.</b> A first attempt
 * mounted this unconditionally inside `SettingsForm`'s own tab loop (keyed on `tab.prefix`) —
 * reverted: that gave every `SettingsForm` consumer carrying any `Tts:*` key (its own extensive
 * jsdom spec suite included) an unmocked `fetch("/api/pronunciations")` as a side effect of merely
 * rendering, corrupting those specs' own sequenced-fetch call counts. `ttsTabExtra` is an inert
 * `ReactNode` prop `SettingsForm` renders without ever importing or knowing about this component —
 * a page that doesn't pass it (every existing spec) renders byte-identical to before, the same
 * injection-point idiom as that form's own `timeZone` prop.
 *
 * Station rows are editable/deletable inline (the `CorrectionsSettingControl` row idiom, adapted
 * to immediate writes: an explicit Save/Cancel per row rather than a call on every keystroke, so a
 * half-typed edit never reaches the network). Persona rows are read-only — the card that imported
 * them is the edit path (F90); {@link PronunciationRuleTableRow} refuses to render an editable
 * draft for one regardless of what its parent computes (PLAN T145 review F1's defense-in-depth
 * layer, on top of {@link isEditingRow} keying on source as well as pattern/word). A shadowed
 * station row (T144's F97.4 merge) stays listed but muted, with no hit count (T142 review ruling:
 * a count only ever attaches to the row actually in effect); a station row that never compiled
 * shows its `Reason` in the danger tone as a row note, not an alarm. Every mutation outcome
 * follows the design-aesthetic Forms rule (validation inline at the field, mutation outcomes as
 * toasts) except two task-specified carve-outs: a 409 renders as a row-level message naming the
 * collision, and a stale 404 toasts AND refreshes the list. Delete additionally goes through
 * `useConfirm()` (SPEC F28.9's plain-words consequence, the `ShowsClient`/`UninstallPackButton`
 * idiom) and a `deletingIdentity` guard so a second click while the first DELETE is still in
 * flight cannot fire twice.
 */
export function PronunciationRulesControl(): ReactNode {
  const confirm = useConfirm();
  const [listState, setListState] = useState<ListState>({ kind: "loading" });
  const [draftPattern, setDraftPattern] = useState("");
  const [draftWord, setDraftWord] = useState("");
  const [draftIpa, setDraftIpa] = useState("");
  const [addFieldErrors, setAddFieldErrors] = useState<Record<string, string[]>>({});
  const [addPending, setAddPending] = useState(false);
  const [editing, setEditing] = useState<EditingState>({ kind: "idle" });
  const [deletingIdentity, setDeletingIdentity] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const rows = await fetchRows();
      setListState({ kind: "loaded", rows });
    } catch {
      setListState({ kind: "error" });
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  async function handleAdd(): Promise<void> {
    setAddPending(true);
    setAddFieldErrors({});
    const outcome = await postRule(draftPattern, draftWord, draftIpa);
    setAddPending(false);

    if (outcome.kind === "ok") {
      setDraftPattern("");
      setDraftWord("");
      setDraftIpa("");
      toast.success("Pronunciation rule added.");
      await refresh();
      return;
    }
    if (outcome.kind === "invalid") {
      setAddFieldErrors(outcome.fieldErrors);
      return;
    }
    if (outcome.kind === "conflict") {
      setAddFieldErrors({ pattern: [outcome.message] });
      return;
    }
    if (outcome.kind === "stale") {
      // Unreachable for an add (no prior identity to go stale) — handled for exhaustiveness.
      toast.error(outcome.message);
      await refresh();
      return;
    }
    toast.error(outcome.message);
  }

  function startEditing(row: PronunciationRuleRow): void {
    setEditing({
      kind: "editing",
      draft: {
        originalPattern: row.pattern,
        originalWord: row.word,
        originalSource: row.source,
        pattern: row.pattern,
        word: row.word,
        ipa: row.ipa,
      },
      fieldErrors: {},
      rowError: null,
      pending: false,
    });
  }

  function cancelEditing(): void {
    setEditing({ kind: "idle" });
  }

  function updateDraft(patch: Partial<Pick<EditingDraft, "pattern" | "word" | "ipa">>): void {
    setEditing((prev) => (prev.kind === "editing" ? { ...prev, draft: { ...prev.draft, ...patch } } : prev));
  }

  async function handleSaveEdit(): Promise<void> {
    if (editing.kind !== "editing") return;
    const { draft } = editing;
    setEditing({ ...editing, pending: true, fieldErrors: {}, rowError: null });
    const outcome = await putRule(draft.originalPattern, draft.originalWord, draft.pattern, draft.word, draft.ipa);

    if (outcome.kind === "ok") {
      toast.success("Pronunciation rule updated.");
      setEditing({ kind: "idle" });
      await refresh();
      return;
    }
    if (outcome.kind === "invalid") {
      setEditing((prev) =>
        prev.kind === "editing" ? { ...prev, pending: false, fieldErrors: outcome.fieldErrors, rowError: null } : prev
      );
      return;
    }
    if (outcome.kind === "conflict") {
      setEditing((prev) =>
        prev.kind === "editing" ? { ...prev, pending: false, fieldErrors: {}, rowError: outcome.message } : prev
      );
      return;
    }
    if (outcome.kind === "stale") {
      toast.error(outcome.message);
      setEditing({ kind: "idle" });
      await refresh();
      return;
    }
    toast.error(outcome.message);
    setEditing((prev) => (prev.kind === "editing" ? { ...prev, pending: false } : prev));
  }

  async function handleDelete(row: PronunciationRuleRow): Promise<void> {
    const confirmed = await confirm({
      title: "Delete pronunciation rule",
      consequence: deleteConsequence(row),
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!confirmed) return;

    const identity = ruleIdentityKey(row);
    setDeletingIdentity(identity);
    const outcome = await deleteRule(row.pattern, row.word);
    setDeletingIdentity(null);

    if (outcome.kind === "ok") {
      toast.success("Pronunciation rule removed.");
      if (isEditingRow(editing, row)) setEditing({ kind: "idle" });
      await refresh();
      return;
    }
    toast.error(toastMessageFor(outcome));
    if (outcome.kind === "stale") {
      await refresh();
    }
  }

  const canAddRow = !addPending && draftPattern.trim() !== "" && draftIpa.trim() !== "";

  /** Enter-to-submit ergonomics for the add row (T145 review round 3) — there is deliberately no
   * `<form>` here for this to ride natively; see the render below for why. */
  function handleAddKeyDown(e: KeyboardEvent<HTMLInputElement>): void {
    if (e.key !== "Enter" || !canAddRow) return;
    e.preventDefault();
    void handleAdd();
  }

  return (
    <section aria-label="Pronunciation rules" className="rounded-[6px] border border-line bg-surface p-5">
      <h2 className="font-display text-[1.1rem] text-ink">Pronunciation rules</h2>
      <p className="mt-1 text-[0.82rem] text-mute">
        Station rules you edit here take effect on the very next spoken line. The active
        persona&rsquo;s own rules win when they name the same pattern and word — a shadowed
        station rule stays listed but is not the one firing.
      </p>

      <div className="mt-4 flex flex-col gap-4">
        {listState.kind === "loading" && (
          <p className="text-[0.85rem] text-mute">Loading pronunciation rules…</p>
        )}

        {listState.kind === "error" && (
          <div className="flex flex-wrap items-center gap-2">
            <p role="alert" className="text-[0.85rem] text-danger">
              Unable to load pronunciation rules.
            </p>
            <Button
              type="button"
              variant="secondary"
              onClick={() => {
                setListState({ kind: "loading" });
                void refresh();
              }}
            >
              Retry
            </Button>
          </div>
        )}

        {listState.kind === "loaded" && (
          <div className="overflow-x-auto rounded-[6px] border border-line">
            <table className="w-full border-collapse text-[0.85rem]">
              <thead>
                <tr className="border-b-2 border-line bg-surface-2">
                  <th scope="col" className={HEADER_CELL}>Pattern</th>
                  <th scope="col" className={HEADER_CELL}>Word</th>
                  <th scope="col" className={HEADER_CELL}>IPA</th>
                  <th scope="col" className={HEADER_CELL}>Source</th>
                  <th scope="col" className={`${HEADER_CELL} text-right tabular-nums`}>Hits</th>
                  <th scope="col" className={HEADER_CELL}>Status</th>
                  <th scope="col" className={HEADER_CELL}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {listState.rows.length === 0 ? (
                  <tr>
                    <td colSpan={COLUMN_COUNT} className="px-3 py-3 text-mute">
                      No pronunciation rules yet — add one below.
                    </td>
                  </tr>
                ) : (
                  listState.rows.map((row) => {
                    const isStation = row.source === "station";
                    const editingThisRow = isStation && isEditingRow(editing, row);
                    const draft = editingThisRow && editing.kind === "editing" ? editing.draft : null;
                    const fieldErrors = editingThisRow && editing.kind === "editing" ? editing.fieldErrors : {};
                    const rowError = editingThisRow && editing.kind === "editing" ? editing.rowError : null;
                    const savePending = editingThisRow && editing.kind === "editing" ? editing.pending : false;
                    const identityKey = ruleIdentityKey(row);
                    return (
                      <PronunciationRuleTableRow
                        key={identityKey}
                        row={row}
                        identityLabel={ruleIdentityLabel(row)}
                        isStation={isStation}
                        muted={!row.inEffect}
                        draft={draft}
                        fieldErrors={fieldErrors}
                        rowError={rowError}
                        savePending={savePending}
                        isDeleting={deletingIdentity === identityKey}
                        onStartEdit={() => startEditing(row)}
                        onCancelEdit={cancelEditing}
                        onUpdateDraft={updateDraft}
                        onSaveEdit={() => void handleSaveEdit()}
                        onDelete={() => void handleDelete(row)}
                      />
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        )}

        {/* T145 review round 3 — deliberately a <div>, never a <form>: this control is mounted
            inside SettingsForm's own page-wide <form> (via ttsTabExtra). A <form> nested inside
            another <form> is invalid HTML — a real browser silently STRIPS the inner element, so
            an onSubmit handler here would never bind, and a type="submit" button would instead
            submit the OUTER SettingsForm natively (a full navigation to bare /settings, no POST
            ever sent, no rule ever created — observed live against the running stack; jsdom
            tolerates the nesting, which is exactly why a jsdom-only suite couldn't catch this).
            Add is a plain type="button" + onClick; onKeyDown below restores Enter-to-submit on
            each field since there is no <form> to provide it natively. */}
        <div className="flex flex-wrap items-end gap-2 border-t border-line pt-3">
          <div className="flex flex-col gap-1">
            <label htmlFor="pronunciation-add-pattern" className="text-[0.78rem] font-semibold text-mute">
              Pattern
            </label>
            <input
              id="pronunciation-add-pattern"
              type="text"
              value={draftPattern}
              onChange={(e) => setDraftPattern(e.currentTarget.value)}
              onKeyDown={handleAddKeyDown}
              disabled={addPending}
              className={CELL_INPUT_CLASSES}
            />
            {(addFieldErrors["pattern"] ?? []).length > 0 && (
              <span role="alert" className={FIELD_ERROR_CLASSES}>
                {(addFieldErrors["pattern"] ?? []).join("; ")}
              </span>
            )}
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor="pronunciation-add-word" className="text-[0.78rem] font-semibold text-mute">
              Word (optional)
            </label>
            <input
              id="pronunciation-add-word"
              type="text"
              placeholder="defaults to Pattern"
              value={draftWord}
              onChange={(e) => setDraftWord(e.currentTarget.value)}
              onKeyDown={handleAddKeyDown}
              disabled={addPending}
              className={CELL_INPUT_CLASSES}
            />
            {(addFieldErrors["word"] ?? []).length > 0 && (
              <span role="alert" className={FIELD_ERROR_CLASSES}>
                {(addFieldErrors["word"] ?? []).join("; ")}
              </span>
            )}
          </div>
          <div className="flex flex-col gap-1">
            <label htmlFor="pronunciation-add-ipa" className="text-[0.78rem] font-semibold text-mute">
              IPA
            </label>
            <input
              id="pronunciation-add-ipa"
              type="text"
              placeholder="/ˈreɪkjaviːk/"
              value={draftIpa}
              onChange={(e) => setDraftIpa(e.currentTarget.value)}
              onKeyDown={handleAddKeyDown}
              disabled={addPending}
              className={CELL_INPUT_CLASSES}
            />
            {(addFieldErrors["ipa"] ?? []).length > 0 && (
              <span role="alert" className={FIELD_ERROR_CLASSES}>
                {(addFieldErrors["ipa"] ?? []).join("; ")}
              </span>
            )}
          </div>
          <Button
            type="button"
            variant="secondary"
            disabled={!canAddRow}
            onClick={() => {
              void handleAdd();
            }}
          >
            {/* "Add pronunciation" rather than "Add rule" (T145 review round 2 note): this tab
                already has CorrectionsSettingControl's own "Add rule" button (Tts:Corrections) —
                two same-named buttons in one tabpanel is an a11y wart this one-word swap avoids. */}
            {addPending ? "Adding…" : "Add pronunciation"}
          </Button>
        </div>
      </div>
    </section>
  );
}

interface PronunciationRuleTableRowProps {
  row: PronunciationRuleRow;
  /** The rule's own display identity ({@link ruleIdentityLabel}) — every aria-label on this row
   * reads from it, never a positional index. */
  identityLabel: string;
  isStation: boolean;
  muted: boolean;
  /** The parent-resolved draft for THIS row, already `null` unless this exact row (source +
   * pattern + word) is the one being edited — {@link isStation} is checked AGAIN below before
   * this is ever rendered as inputs (PLAN T145 review F1's defense-in-depth layer: a persona row
   * must never show an editable draft, even if a future change to the parent's resolution logic
   * got that wrong). */
  draft: Pick<EditingDraft, "pattern" | "word" | "ipa"> | null;
  fieldErrors: Record<string, string[]>;
  rowError: string | null;
  savePending: boolean;
  /** True while THIS row's own DELETE request is in flight — disables its Delete button so a
   * second click cannot fire a second request (PLAN T145 review F5). */
  isDeleting: boolean;
  onStartEdit: () => void;
  onCancelEdit: () => void;
  onUpdateDraft: (patch: Partial<Pick<EditingDraft, "pattern" | "word" | "ipa">>) => void;
  onSaveEdit: () => void;
  onDelete: () => void;
}

/**
 * One pronunciation-rule row, plus its own conflict-message row when a save collides (T144 review
 * ruling: a 409 is row-level, not a toast). A pure presenter (PLAN T145 review should-fix): every
 * value it renders — `draft`, `fieldErrors`, `rowError`, `savePending`, `isDeleting` — arrives
 * already resolved from the parent, which alone owns `editing`/list/delete state; this component
 * never reads `EditingState` itself, so there is exactly ONE place (the parent's per-row
 * resolution, immediately above where this is invoked) that decides which row is being edited.
 */
function PronunciationRuleTableRow({
  row,
  identityLabel,
  isStation,
  muted,
  draft,
  fieldErrors,
  rowError,
  savePending,
  isDeleting,
  onStartEdit,
  onCancelEdit,
  onUpdateDraft,
  onSaveEdit,
  onDelete,
}: PronunciationRuleTableRowProps): ReactNode {
  // Defense-in-depth (PLAN T145 review F1): a persona row can NEVER render an editable draft,
  // regardless of what `draft` the parent passed in.
  const editableDraft = isStation ? draft : null;
  const textCellClasses = muted ? "text-mute" : "text-ink";

  return (
    <>
      <tr className="border-b border-line last:border-b-0">
        <td className={`py-1.5 pr-2 pl-3 ${textCellClasses}`}>
          {editableDraft !== null ? (
            <>
              <input
                type="text"
                aria-label={`Pattern for ${identityLabel}`}
                value={editableDraft.pattern}
                onChange={(e) => onUpdateDraft({ pattern: e.currentTarget.value })}
                disabled={savePending}
                className={CELL_INPUT_CLASSES}
              />
              {(fieldErrors["pattern"] ?? []).length > 0 && (
                <span role="alert" className={FIELD_ERROR_CLASSES}>
                  {(fieldErrors["pattern"] ?? []).join("; ")}
                </span>
              )}
            </>
          ) : (
            row.pattern
          )}
        </td>
        <td className={`py-1.5 pr-2 ${textCellClasses}`}>
          {editableDraft !== null ? (
            <>
              <input
                type="text"
                aria-label={`Word for ${identityLabel}`}
                value={editableDraft.word}
                onChange={(e) => onUpdateDraft({ word: e.currentTarget.value })}
                disabled={savePending}
                className={CELL_INPUT_CLASSES}
              />
              {(fieldErrors["word"] ?? []).length > 0 && (
                <span role="alert" className={FIELD_ERROR_CLASSES}>
                  {(fieldErrors["word"] ?? []).join("; ")}
                </span>
              )}
            </>
          ) : (
            row.word
          )}
        </td>
        <td className={`py-1.5 pr-2 ${textCellClasses}`}>
          {editableDraft !== null ? (
            <>
              <input
                type="text"
                aria-label={`IPA for ${identityLabel}`}
                value={editableDraft.ipa}
                onChange={(e) => onUpdateDraft({ ipa: e.currentTarget.value })}
                disabled={savePending}
                className={CELL_INPUT_CLASSES}
              />
              {(fieldErrors["ipa"] ?? []).length > 0 && (
                <span role="alert" className={FIELD_ERROR_CLASSES}>
                  {(fieldErrors["ipa"] ?? []).join("; ")}
                </span>
              )}
            </>
          ) : (
            row.ipa
          )}
        </td>
        <td className="py-1.5 pr-2">
          <RuleSourceChip source={row.source} />
        </td>
        <td className="py-1.5 pr-3 text-right tabular-nums text-mute">
          {row.inEffect ? row.hitCount ?? 0 : "—"}
        </td>
        <td className="py-1.5 pr-2 text-[0.78rem]">
          <RuleStatusNote row={row} />
        </td>
        <td className="py-1.5 pr-3">
          <div className="flex flex-wrap items-center gap-2">
            {!isStation ? (
              <span className="text-[0.78rem] text-mute">Edit on the persona&rsquo;s card</span>
            ) : editableDraft !== null ? (
              <>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label={`Save ${identityLabel}`}
                  disabled={savePending}
                  onClick={onSaveEdit}
                >
                  {savePending ? "Saving…" : "Save"}
                </Button>
                <Button type="button" variant="secondary" disabled={savePending} onClick={onCancelEdit}>
                  Cancel
                </Button>
              </>
            ) : (
              <>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label={`Edit ${identityLabel}`}
                  onClick={onStartEdit}
                >
                  Edit
                </Button>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label={`Delete ${identityLabel}`}
                  disabled={isDeleting}
                  onClick={onDelete}
                >
                  {isDeleting ? "Deleting…" : "Delete"}
                </Button>
              </>
            )}
          </div>
        </td>
      </tr>
      {rowError !== null && (
        <tr>
          <td colSpan={COLUMN_COUNT} className="px-3 pb-2 pt-0">
            <p role="alert" className={FIELD_ERROR_CLASSES}>
              {rowError}
            </p>
          </td>
        </tr>
      )}
    </>
  );
}

/** Brass source chip (the design language's structure color) — "Station" or "Persona". */
function RuleSourceChip({ source }: { source: PronunciationRuleRow["source"] }): ReactNode {
  const label = source === "station" ? "Station" : "Persona";
  return (
    <Chip aria-label={`Source: ${label}`} className="border-accent-2 text-accent-2">
      {label}
    </Chip>
  );
}

/** The row-level note for a shadowed (quiet) or never-compiled (danger tone, not an alarm) rule —
 * blank for an ordinary in-effect row. */
function RuleStatusNote({ row }: { row: PronunciationRuleRow }): ReactNode {
  if (row.reason !== null) {
    return <span className="text-danger">{row.reason}</span>;
  }
  if (!row.inEffect) {
    return <span className="text-mute">Not in effect — shadowed by a persona rule.</span>;
  }
  return null;
}
