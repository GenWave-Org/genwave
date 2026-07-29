"use client";

import { useEffect, useState, type ChangeEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import type { SettingControlProps } from "./settings-types";

/** One `Tts:Corrections` rule row — mirrors GenWave.Tts.SpeechCorrection's wire shape. */
interface CorrectionRow {
  from: string;
  to: string;
}

/** One row of `GET /api/tts/corrections-stats` (SPEC F68.7). */
interface CorrectionStat {
  from: string;
  fired: number;
}

type StatsStatus =
  | { kind: "loading" }
  | { kind: "loaded"; byFrom: Map<string, number> }
  | { kind: "error" };

type PreviewStatus =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "loaded"; spoken: string }
  | { kind: "error"; message: string };

function isCorrectionRow(raw: unknown): raw is CorrectionRow {
  if (typeof raw !== "object" || raw === null) return false;
  const obj = raw as Record<string, unknown>;
  return typeof obj["from"] === "string" && typeof obj["to"] === "string";
}

function isCorrectionStatList(raw: unknown): raw is CorrectionStat[] {
  return (
    Array.isArray(raw) &&
    raw.every((entry) => {
      if (typeof entry !== "object" || entry === null) return false;
      const obj = entry as Record<string, unknown>;
      return typeof obj["from"] === "string" && typeof obj["fired"] === "number";
    })
  );
}

function readSpokenField(raw: unknown): string | null {
  if (typeof raw !== "object" || raw === null) return null;
  const spoken = (raw as Record<string, unknown>)["spoken"];
  return typeof spoken === "string" ? spoken : null;
}

/**
 * Parses the staged `Tts:Corrections` JSON-array-string into rows. Any parse failure or
 * non-array/non-`{from,to}` shape degrades to an empty table rather than throwing — the operator
 * can always start fresh (mirrors SettingsForm's own defensive `parseLibraryIds` convention).
 */
function parseCorrections(value: string): CorrectionRow[] {
  if (value.trim() === "") return [];
  try {
    const parsed: unknown = JSON.parse(value);
    return Array.isArray(parsed) ? parsed.filter(isCorrectionRow) : [];
  } catch {
    return [];
  }
}

function serializeCorrections(rows: CorrectionRow[]): string {
  return JSON.stringify(rows.map((row) => ({ from: row.from, to: row.to })));
}

const CELL_INPUT_CLASSES =
  "h-9 w-full rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";
const HEADER_CELL =
  "py-2 pr-3 pl-3 text-left text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";

/**
 * `Tts:Corrections` table editor (SPEC F68.5–F68.7, STORY-186; registered in SettingsForm's
 * per-key control-override registry, F54.1). Add/edit/delete all stage plain JSON via the SAME
 * `onChange(value)` callback every other registered control uses (SETTING_CONTROL_REGISTRY), so
 * Save persists it through the unmodified PUT /api/settings batch (F54.4, AC1) — this control never
 * talks to the network for CRUD, only for its two read-only extras below.
 *
 * Staging is deliberate (gh-#139): every rule change — add, edit, delete — stages until the
 * page-wide Save settings, exactly like every other control on this page. What makes it
 * unmistakable is the `isDirty` prop (SettingsForm's own save-diff verdict, re-baselined after
 * each successful save per gh-#140): while dirty, an "Unsaved changes" badge renders over the
 * table and the Preview note turns prominent, since preview runs against the SAVED rules.
 *
 * Two supplementary, best-effort reads — both degrade silently, since this is an editor, not a
 * feature that a missing endpoint should ever block:
 *   - Per-rule fired counts from `GET /api/tts/corrections-stats` (SPEC F68.7, AC3), shown next to
 *     each row.
 *   - A "preview spoken form" action against `POST /api/tts/normalize-preview` (SPEC F68.6, AC2) —
 *     that endpoint runs the REAL SpeechText.Normalize against the CURRENTLY SAVED corrections
 *     snapshot, not this control's staged edits, so the note under the button says which rules
 *     it used whenever the two could differ.
 */
export function CorrectionsSettingControl({
  controlId,
  value,
  onChange,
  disabled,
  isDirty = false,
}: SettingControlProps): ReactNode {
  const rows = parseCorrections(value);

  const [draftFrom, setDraftFrom] = useState("");
  const [draftTo, setDraftTo] = useState("");
  const [statsStatus, setStatsStatus] = useState<StatsStatus>({ kind: "loading" });
  const [previewText, setPreviewText] = useState("");
  const [previewStatus, setPreviewStatus] = useState<PreviewStatus>({ kind: "idle" });

  useEffect(() => {
    let cancelled = false;

    async function loadStats(): Promise<void> {
      try {
        const resp = await fetch("/api/tts/corrections-stats");
        if (!resp.ok) {
          if (!cancelled) setStatsStatus({ kind: "error" });
          return;
        }
        const raw = (await resp.json()) as unknown;
        if (!isCorrectionStatList(raw)) {
          if (!cancelled) setStatsStatus({ kind: "error" });
          return;
        }
        if (!cancelled) {
          setStatsStatus({
            kind: "loaded",
            byFrom: new Map(raw.map((entry) => [entry.from.toLowerCase(), entry.fired])),
          });
        }
      } catch {
        if (!cancelled) setStatsStatus({ kind: "error" });
      }
    }

    void loadStats();
    return () => {
      cancelled = true;
    };
  }, []);

  function firedCountFor(from: string): number | null {
    if (statsStatus.kind !== "loaded") return null;
    return statsStatus.byFrom.get(from.trim().toLowerCase()) ?? 0;
  }

  function updateRow(index: number, patch: Partial<CorrectionRow>): void {
    onChange(serializeCorrections(rows.map((row, i) => (i === index ? { ...row, ...patch } : row))));
  }

  function deleteRow(index: number): void {
    onChange(serializeCorrections(rows.filter((_, i) => i !== index)));
  }

  function addRow(): void {
    const from = draftFrom.trim();
    if (from === "") return;
    onChange(serializeCorrections([...rows, { from, to: draftTo }]));
    setDraftFrom("");
    setDraftTo("");
  }

  async function runPreview(): Promise<void> {
    if (previewText.trim() === "") return;
    setPreviewStatus({ kind: "loading" });
    try {
      const resp = await fetch("/api/tts/normalize-preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ text: previewText }),
      });
      if (!resp.ok) {
        setPreviewStatus({ kind: "error", message: `Preview unavailable (${resp.status}).` });
        return;
      }
      const spoken = readSpokenField((await resp.json()) as unknown);
      if (spoken === null) {
        setPreviewStatus({ kind: "error", message: "Preview response was unreadable." });
        return;
      }
      setPreviewStatus({ kind: "loaded", spoken });
    } catch {
      setPreviewStatus({ kind: "error", message: "Network error — check your connection." });
    }
  }

  const canAddRow = !disabled && draftFrom.trim() !== "";

  return (
    <div id={controlId} className="flex flex-col gap-4">
      {isDirty && (
        <p
          data-testid="corrections-dirty-notice"
          role="status"
          aria-live="polite"
          className="flex flex-wrap items-center gap-2 text-[0.78rem] text-accent-2"
        >
          <span className="inline-flex items-center rounded-[999px] border border-accent-2 bg-transparent px-2.5 py-1 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
            Unsaved changes
          </span>
          Rule changes are staged — Save settings to apply them.
        </p>
      )}

      <div className="overflow-x-auto rounded-[6px] border border-line">
        <table className="w-full border-collapse text-[0.85rem]">
          <thead>
            <tr className="border-b-2 border-line bg-surface-2">
              <th scope="col" className={HEADER_CELL}>
                From
              </th>
              <th scope="col" className={HEADER_CELL}>
                To
              </th>
              <th scope="col" className={`${HEADER_CELL} text-right tabular-nums`}>
                Fired
              </th>
              <th scope="col" className={HEADER_CELL}>
                Actions
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={4} className="px-3 py-3 text-mute">
                  No corrections yet — add one below.
                </td>
              </tr>
            ) : (
              rows.map((row, index) => {
                const fired = firedCountFor(row.from);
                return (
                  <tr key={index} className="border-b border-line last:border-b-0">
                    <td className="py-1.5 pr-2 pl-3">
                      <input
                        type="text"
                        aria-label={`From text for rule ${index + 1}`}
                        value={row.from}
                        onChange={(e: ChangeEvent<HTMLInputElement>) =>
                          updateRow(index, { from: e.currentTarget.value })
                        }
                        disabled={disabled}
                        className={CELL_INPUT_CLASSES}
                      />
                    </td>
                    <td className="py-1.5 pr-2">
                      <input
                        type="text"
                        aria-label={`To text for rule ${index + 1}`}
                        value={row.to}
                        onChange={(e: ChangeEvent<HTMLInputElement>) =>
                          updateRow(index, { to: e.currentTarget.value })
                        }
                        disabled={disabled}
                        className={CELL_INPUT_CLASSES}
                      />
                    </td>
                    <td className="py-1.5 pr-3 text-right tabular-nums text-mute">
                      {fired === null ? "—" : fired}
                    </td>
                    <td className="py-1.5 pr-3">
                      <Button
                        type="button"
                        variant="secondary"
                        aria-label={`Delete rule ${index + 1}`}
                        disabled={disabled}
                        onClick={() => deleteRow(index)}
                      >
                        Delete
                      </Button>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      <div className="flex flex-wrap items-end gap-2">
        <div className="flex flex-col gap-1">
          <label htmlFor={`${controlId}-add-from`} className="text-[0.78rem] font-semibold text-mute">
            From
          </label>
          <input
            id={`${controlId}-add-from`}
            type="text"
            value={draftFrom}
            onChange={(e) => setDraftFrom(e.currentTarget.value)}
            disabled={disabled}
            className={CELL_INPUT_CLASSES}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor={`${controlId}-add-to`} className="text-[0.78rem] font-semibold text-mute">
            To
          </label>
          <input
            id={`${controlId}-add-to`}
            type="text"
            value={draftTo}
            onChange={(e) => setDraftTo(e.currentTarget.value)}
            disabled={disabled}
            className={CELL_INPUT_CLASSES}
          />
        </div>
        <Button type="button" variant="secondary" disabled={!canAddRow} onClick={addRow}>
          Add rule
        </Button>
      </div>

      <div className="flex flex-col gap-2 border-t border-line pt-3">
        <label htmlFor={`${controlId}-preview-text`} className="text-[0.78rem] font-semibold text-mute">
          Preview spoken form
        </label>
        <div className="flex flex-wrap items-center gap-2">
          <input
            id={`${controlId}-preview-text`}
            type="text"
            placeholder="Coming up, a deep cut from MacLeod."
            value={previewText}
            onChange={(e) => setPreviewText(e.currentTarget.value)}
            className={`${CELL_INPUT_CLASSES} max-w-md`}
          />
          <Button
            type="button"
            variant="secondary"
            disabled={previewText.trim() === "" || previewStatus.kind === "loading"}
            onClick={() => {
              void runPreview();
            }}
          >
            {previewStatus.kind === "loading" ? "Previewing…" : "Preview"}
          </Button>
        </div>
        {isDirty ? (
          <p
            data-testid="corrections-preview-note"
            role="status"
            className="text-[0.78rem] font-semibold text-accent-2"
          >
            Previews with the last-saved rules — the staged changes above are not included until
            you Save settings.
          </p>
        ) : (
          <p data-testid="corrections-preview-note" className="text-[0.78rem] text-mute">
            Previews with your saved rules.
          </p>
        )}
        {previewStatus.kind === "loaded" && (
          <p data-testid="corrections-preview-output" className="text-[0.85rem] text-ink">
            {previewStatus.spoken}
          </p>
        )}
        {previewStatus.kind === "error" && (
          <p role="alert" className="text-[0.78rem] text-danger">
            {previewStatus.message}
          </p>
        )}
      </div>
    </div>
  );
}
