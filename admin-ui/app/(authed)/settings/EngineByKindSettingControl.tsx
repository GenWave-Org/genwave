"use client";

import { useState, type ChangeEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import type { SettingControlProps } from "./settings-types";

/**
 * The closed set of speech kinds the backend accepts as `Tts:EngineByKind` keys — mirrors
 * `GenWave.Core.Domain.SegmentKind` (src/GenWave.Abstractions/Domain/SegmentKind.cs) EXACTLY,
 * in declaration order. `SettingValidator.IsValidEngineByKindMap` rejects any key that is not
 * one of these enum NAMES (case-insensitively; numeric strings are explicitly refused), so the
 * dropdown must never offer a value outside this list. Hardcoded because the backend exposes no
 * endpoint enumerating SegmentKind; the xUnit/jest parity ethos applies — a new enum member
 * lands here by hand, and the spec's independently-authored copy catches a one-sided edit.
 */
const SEGMENT_KINDS = [
  "StationId",
  "LeadIn",
  "BackAnnounce",
  "TimeDate",
  "SignOff",
  "SignOn",
] as const;

type SegmentKindName = (typeof SEGMENT_KINDS)[number];

/**
 * The engines the backend accepts as `Tts:EngineByKind` values — mirrors
 * `SettingValidator.IsValidEngineByKindMap` (src/GenWave.Host/Configuration/SettingValidator.cs),
 * which accepts exactly "kokoro" | "piper" case-insensitively; the read path
 * (`GenWave.Tts.TtsEngineByKindProvider`) normalizes to the same two lowercase
 * `GenWave.Tts.DependencyNames` constants this list carries. Hardcoded — there is no endpoint
 * enumerating supported engines.
 */
const ENGINES = ["kokoro", "piper"] as const;

type EngineName = (typeof ENGINES)[number];

/** Human labels for the option text; the option VALUE stays the lowercase wire constant. */
const ENGINE_LABELS: Record<EngineName, string> = {
  kokoro: "Kokoro",
  piper: "Piper",
};

/** One staged override row — a (speech kind → engine) pin. */
interface EngineOverrideRow {
  kind: SegmentKindName;
  engine: EngineName;
}

/** Case-insensitive canonicalization to a SegmentKind enum NAME, or null for anything else. */
function canonicalKind(raw: string): SegmentKindName | null {
  const lowered = raw.trim().toLowerCase();
  return SEGMENT_KINDS.find((kind) => kind.toLowerCase() === lowered) ?? null;
}

/** Case-insensitive canonicalization to a known lowercase engine name, or null. */
function canonicalEngine(raw: unknown): EngineName | null {
  if (typeof raw !== "string") return null;
  const lowered = raw.trim().toLowerCase();
  return ENGINES.find((engine) => engine === lowered) ?? null;
}

/**
 * Parses the staged `Tts:EngineByKind` JSON-object-string into rows, preserving the object's
 * entry order. Tolerant exactly the way the backend read path is (TtsEngineByKindProvider):
 * kind names and engine values match case-insensitively and render canonicalized; an unknown
 * kind, unknown engine, or non-string value drops that ONE entry; malformed JSON or a non-object
 * degrades to an empty table rather than throwing (the `parseCorrections` convention). Parsing
 * never fires `onChange` on its own — the staged string is only rewritten when the operator
 * actually edits, so an untouched control can never invent a dirty diff.
 */
function parseEngineByKind(value: string): EngineOverrideRow[] {
  if (value.trim() === "") return [];
  try {
    const parsed: unknown = JSON.parse(value);
    if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed)) return [];
    const rows: EngineOverrideRow[] = [];
    for (const [rawKind, rawEngine] of Object.entries(parsed as Record<string, unknown>)) {
      const kind = canonicalKind(rawKind);
      const engine = canonicalEngine(rawEngine);
      if (kind === null || engine === null) continue;
      if (rows.some((row) => row.kind === kind)) continue;
      rows.push({ kind, engine });
    }
    return rows;
  } catch {
    return [];
  }
}

/**
 * Serializes rows back to the exact wire shape the backend parses today — a compact JSON object
 * of canonical SegmentKind names to lowercase engine names, e.g. `{"StationId":"piper"}`
 * (StationSettingsAllowlist's own documented example, byte for byte). No rows serializes to `""`,
 * the allowlist's seeded default — "no per-kind overrides", identical to pre-feature routing.
 */
function serializeEngineByKind(rows: EngineOverrideRow[]): string {
  if (rows.length === 0) return "";
  return JSON.stringify(Object.fromEntries(rows.map((row) => [row.kind, row.engine])));
}

const CELL_SELECT_CLASSES =
  "h-9 w-full rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";
const HEADER_CELL =
  "py-2 pr-3 pl-3 text-left text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";

/**
 * `Tts:EngineByKind` structured editor (gh-#146; registered in SettingsForm's per-key
 * control-override registry, F54.1) — replaces the freeform JSON text input with rows of
 * (speech-kind dropdown × engine dropdown). Both dimensions are closed sets the backend
 * validates (`SettingValidator.IsValidEngineByKindMap`), so a typo that used to ship silently
 * now cannot be expressed at all.
 *
 * Same contract as `CorrectionsSettingControl`: every change — add, re-pin, delete — stages
 * plain JSON through the shared `onChange(value)` callback and rides the page-wide Save
 * settings PUT batch (F54.4); this control never talks to the network. Staging is made
 * unmistakable by the same `isDirty` dirty-pill pattern (gh-#139/gh-#140): SettingsForm's own
 * save-diff verdict renders the "Unsaved changes" badge, re-baselined after each successful
 * save.
 *
 * A kind can be pinned at most once (the wire shape is a JSON object — duplicate keys cannot
 * round-trip), so each kind dropdown only offers kinds no OTHER row currently pins.
 */
export function EngineByKindSettingControl({
  controlId,
  value,
  onChange,
  disabled,
  isDirty = false,
}: SettingControlProps): ReactNode {
  const rows = parseEngineByKind(value);

  const [draftKind, setDraftKind] = useState<string>("");
  const [draftEngine, setDraftEngine] = useState<EngineName>("kokoro");

  const usedKinds = new Set(rows.map((row) => row.kind));
  const availableKinds = SEGMENT_KINDS.filter((kind) => !usedKinds.has(kind));

  function updateRow(index: number, patch: Partial<EngineOverrideRow>): void {
    onChange(
      serializeEngineByKind(rows.map((row, i) => (i === index ? { ...row, ...patch } : row)))
    );
  }

  function deleteRow(index: number): void {
    onChange(serializeEngineByKind(rows.filter((_, i) => i !== index)));
  }

  function addRow(): void {
    const kind = canonicalKind(draftKind);
    if (kind === null || usedKinds.has(kind)) return;
    onChange(serializeEngineByKind([...rows, { kind, engine: draftEngine }]));
    setDraftKind("");
    setDraftEngine("kokoro");
  }

  const canAddRow = !disabled && canonicalKind(draftKind) !== null;

  return (
    <div id={controlId} className="flex flex-col gap-4">
      {isDirty && (
        <p
          data-testid="engine-by-kind-dirty-notice"
          role="status"
          aria-live="polite"
          className="flex flex-wrap items-center gap-2 text-[0.78rem] text-accent-2"
        >
          <span className="inline-flex items-center rounded-[999px] border border-accent-2 bg-transparent px-2.5 py-1 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
            Unsaved changes
          </span>
          Engine overrides are staged — Save settings to apply them.
        </p>
      )}

      <div className="overflow-x-auto rounded-[6px] border border-line">
        <table className="w-full border-collapse text-[0.85rem]">
          <thead>
            <tr className="border-b-2 border-line bg-surface-2">
              <th scope="col" className={HEADER_CELL}>
                Speech kind
              </th>
              <th scope="col" className={HEADER_CELL}>
                Engine
              </th>
              <th scope="col" className={HEADER_CELL}>
                Actions
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td colSpan={3} className="px-3 py-3 text-mute">
                  No overrides — every kind uses the normal Kokoro-first, Piper-fallback routing.
                </td>
              </tr>
            ) : (
              rows.map((row, index) => (
                <tr key={row.kind} className="border-b border-line last:border-b-0">
                  <td className="py-1.5 pr-2 pl-3">
                    <select
                      aria-label={`Speech kind for override ${index + 1}`}
                      value={row.kind}
                      onChange={(e: ChangeEvent<HTMLSelectElement>) => {
                        const kind = canonicalKind(e.currentTarget.value);
                        if (kind !== null) updateRow(index, { kind });
                      }}
                      disabled={disabled}
                      className={CELL_SELECT_CLASSES}
                    >
                      {/* This row's own kind plus every kind no other row pins — a duplicate
                          key cannot survive JSON-object serialization, so it is unpickable. */}
                      {SEGMENT_KINDS.filter(
                        (kind) => kind === row.kind || !usedKinds.has(kind)
                      ).map((kind) => (
                        <option key={kind} value={kind}>
                          {kind}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="py-1.5 pr-2">
                    <select
                      aria-label={`Engine for override ${index + 1}`}
                      value={row.engine}
                      onChange={(e: ChangeEvent<HTMLSelectElement>) => {
                        const engine = canonicalEngine(e.currentTarget.value);
                        if (engine !== null) updateRow(index, { engine });
                      }}
                      disabled={disabled}
                      className={CELL_SELECT_CLASSES}
                    >
                      {ENGINES.map((engine) => (
                        <option key={engine} value={engine}>
                          {ENGINE_LABELS[engine]}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="py-1.5 pr-3">
                    <Button
                      type="button"
                      variant="secondary"
                      aria-label={`Delete override ${index + 1}`}
                      disabled={disabled}
                      onClick={() => deleteRow(index)}
                    >
                      Delete
                    </Button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {availableKinds.length === 0 ? (
        <p className="text-[0.78rem] text-mute">Every speech kind already has an override.</p>
      ) : (
        <div className="flex flex-wrap items-end gap-2">
          <div className="flex flex-col gap-1">
            <label
              htmlFor={`${controlId}-add-kind`}
              className="text-[0.78rem] font-semibold text-mute"
            >
              Speech kind
            </label>
            <select
              id={`${controlId}-add-kind`}
              value={draftKind}
              onChange={(e) => setDraftKind(e.currentTarget.value)}
              disabled={disabled}
              className={CELL_SELECT_CLASSES}
            >
              <option value="">Choose a kind…</option>
              {availableKinds.map((kind) => (
                <option key={kind} value={kind}>
                  {kind}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-1">
            <label
              htmlFor={`${controlId}-add-engine`}
              className="text-[0.78rem] font-semibold text-mute"
            >
              Engine
            </label>
            <select
              id={`${controlId}-add-engine`}
              value={draftEngine}
              onChange={(e) => {
                const engine = canonicalEngine(e.currentTarget.value);
                if (engine !== null) setDraftEngine(engine);
              }}
              disabled={disabled}
              className={CELL_SELECT_CLASSES}
            >
              {ENGINES.map((engine) => (
                <option key={engine} value={engine}>
                  {ENGINE_LABELS[engine]}
                </option>
              ))}
            </select>
          </div>
          <Button type="button" variant="secondary" disabled={!canAddRow} onClick={addRow}>
            Add override
          </Button>
        </div>
      )}
    </div>
  );
}
