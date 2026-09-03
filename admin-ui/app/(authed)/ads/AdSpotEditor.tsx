"use client";

import { useMemo, useState, type FormEvent, type ReactNode } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import { Button } from "@/components/ui/button";
import { useRestoreFocus } from "@/lib/use-restore-focus";
import {
  createAdSpot,
  updateAdSpot,
  type AdSpotDto,
  type AdSpotSaveBody,
  type AdVoicePlanEntry,
} from "@/lib/ads-api";
import { BedPicker, type BedCandidate } from "../safe-content/BedPicker";
import { VoiceControl } from "../safe-content/VoiceControl";
import { FieldRow, FIELD_INPUT_CLASSES, FIELD_LABEL_CLASSES } from "./FieldRow";

const SPOT_SECONDS_OPTIONS = [15, 30, 60] as const;
const DEFAULT_SPOT_SECONDS = 30;

export interface AdSpotEditorProps {
  /** The spot being edited, or `null` to create a new owner draft (SPEC F162.1's "create/edit
   * drafts"). */
  initial: AdSpotDto | null;
  onSaved: (spot: AdSpotDto) => void;
  onCancel: () => void;
}

/** The server's own tag shape (`GenWave.Ads.AdScriptParser.TagPattern`) — starts with a letter,
 * then any run of uppercase letters/digits. */
const AD_SCRIPT_TAG_PATTERN = /^[A-Z][A-Z0-9]*$/;

/**
 * Parses `TAG: line` script lines client-side into the distinct tags, in first-appearance order —
 * DISPLAY SUGAR ONLY (PLAN T404's own ruling): this drives which voice pickers render, nothing
 * more. `AdScriptValidator` (`AdScriptParser.ParseLine`) on the server is the real parser and is
 * what actually accepts or refuses the script; a malformed line here simply fails to produce a
 * picker for it — the save-time 400 is what tells the operator why.
 *
 * <b>PLAN T404 review fold (h) — aligned to the server's EXACT algorithm</b> (`ParseLine`: split at
 * the FIRST `:`, trim both sides, then match the tag against {@link AD_SCRIPT_TAG_PATTERN}) rather
 * than the earlier `/^([A-Z0-9]+):/` shape, which required the colon to sit flush against the tag
 * with no intervening space. That divergence was silent and one-directional: a line like
 * `"ANNOUNCER : Hello"` (a space before the colon — the server tolerates it, since it trims after
 * splitting) failed to produce a picker at all, so the operator's only signal that a tag existed
 * was the render silently falling back to the station voice, with no visible reason why. Matching
 * the server's own split-then-trim shape here means every tag the server WILL accept gets offered a
 * picker, and every tag the server WOULD refuse (fails {@link AD_SCRIPT_TAG_PATTERN}, e.g. leading
 * digit) gets none here either — the two can no longer silently disagree.
 */
function parseScriptTags(script: string): string[] {
  const tags: string[] = [];
  for (const rawLine of script.split("\n")) {
    const trimmedLine = rawLine.trim();
    const colonIndex = trimmedLine.indexOf(":");
    if (colonIndex <= 0) continue;

    const tag = trimmedLine.slice(0, colonIndex).trim();
    if (AD_SCRIPT_TAG_PATTERN.test(tag) && !tags.includes(tag)) tags.push(tag);
  }
  return tags;
}

function initialVoicesByTag(entries: readonly AdVoicePlanEntry[] | null): Record<string, string> {
  const byTag: Record<string, string> = {};
  if (entries === null) return byTag;
  for (const entry of entries) byTag[entry.tag] = entry.voiceId;
  return byTag;
}

/** A bed already on the row carries only its id (`AdSpotDto.bedMediaId`) — no title/artist, since
 * the Ads wire shape never joins the bed row's own metadata. `BedPicker`'s "selected" view falls
 * back to `#<id>` for a candidate with no title (its own `candidateLabel` remarks); this is that
 * fallback, not a defect — a fuller label would need a second `GET /api/media/{id}` this task's
 * scope doesn't call for. Picking a new bed (or clearing) always carries the real title. */
function initialBedCandidate(bedMediaId: number | null): BedCandidate | null {
  return bedMediaId === null ? null : { mediaId: bedMediaId, title: null, artist: null };
}

type SaveStatus =
  | { kind: "idle" }
  | { kind: "pending" }
  | { kind: "error"; detail: string; field?: string; ruleId?: string };

/**
 * Create/edit modal for one ad spot (SPEC F162.1; STORY-392 AC2; PLAN T404) — brand, title, brief,
 * script, a per-tag voice cast parsed from the script, spot length, and an optional bed via
 * `BedPicker`. Bespoke Radix `Dialog` markup, mirroring `gardener/FileActionDialog.tsx`'s own
 * reasoning: this content (six fields plus a variable-length voice cast) is wider than a yes/no
 * prompt, so it doesn't reuse `DialogShell`.
 *
 * One component for both create (`initial === null`, `POST /api/ads`) and edit
 * (`PATCH /api/ads/{id}`, If-Match from `initial.version`) — the `AdSpotSaveRequest` wire shape is
 * already shared both ways (`AdsController`'s own remarks); this mirrors that on the client.
 *
 * `VoiceControl`/`BedPicker` are reused verbatim from `safe-content/` (PLAN T404's own judgment:
 * both already take plain props with no Safe-Content-specific coupling — `VoiceControl` already has
 * a second call site, `PersonasClient` — so this is the established reuse posture, not a fresh
 * page-scoped copy).
 *
 * <b>PLAN T404 review F1 — the sparse-PATCH "can't clear" gap, made honest, not fixed.</b>
 * `AdsController.Update`/`AdSpotEdit` treat a `null` field as "leave unchanged" (the
 * `MediaPatch` sparse-update precedent) — this is correct and by design for the API (a recorded
 * carry-forward the orchestrator files separately; this task does not widen that surface), but it
 * means a naive "empty the field, submit" gesture in THIS editor would silently do nothing while
 * looking like it cleared the value. Three fields carry that risk once `initial !== null`, and each
 * is closed a different way, chosen by what the control itself can express:
 * <list type="bullet">
 * <item><b>Bed</b> — `BedPicker`'s `allowClear` prop is `false` whenever editing a row that already
 * has a committed bed ({@link canClearBed}): the Clear affordance is HIDDEN rather than present-
 * but-inert, so the control itself never offers a gesture the api would silently ignore. Replacing
 * with a DIFFERENT bed still works (a non-null `bedMediaId` always overwrites honestly); only the
 * null-out-a-committed-bed gesture is unavailable here.</item>
 * <item><b>Voice cast</b> — a tag that already had an explicit cast when the dialog opened is
 * PINNED ({@link pinnedTags}): reverting its `VoiceControl` back to "Station default" is refused by
 * {@link handleVoiceChange} (the select snaps back to its pinned value), with an inline note saying
 * why, rather than silently accepting a choice that would drop out of the submitted `voicePlan`
 * array as "no explicit cast" and read as "unchanged" server-side.</item>
 * <item><b>Brief/script</b> — emptying a field that started non-null is refused AT SUBMIT
 * ({@link handleSubmit}) with an inline message naming the limitation, rather than accepted and
 * silently dropped. Unlike bed/voice, there is no single control gesture to hide here (any keypress
 * can empty a textarea), so this is the one guard that fires at the submit boundary instead of at
 * the control.</item>
 * </list>
 */
export function AdSpotEditor({ initial, onSaved, onCancel }: AdSpotEditorProps): ReactNode {
  const restoreFocus = useRestoreFocus("on-mount");

  const [brand, setBrand] = useState(initial?.brand ?? "");
  const [title, setTitle] = useState(initial?.title ?? "");
  const [brief, setBrief] = useState(initial?.brief ?? "");
  const [script, setScript] = useState(initial?.script ?? "");
  const [spotSeconds, setSpotSeconds] = useState<number>(initial?.spotSeconds ?? DEFAULT_SPOT_SECONDS);
  const [voicesByTag, setVoicesByTag] = useState<Record<string, string>>(() =>
    initialVoicesByTag(initial?.voicePlan ?? null)
  );
  const [bed, setBed] = useState<BedCandidate | null>(initialBedCandidate(initial?.bedMediaId ?? null));
  const [status, setStatus] = useState<SaveStatus>({ kind: "idle" });

  const tags = useMemo(() => parseScriptTags(script), [script]);
  const isPending = status.kind === "pending";

  // F1 — every tag that already had an explicit cast when this dialog opened; see the class
  // remarks' "Voice cast" bullet. Fixed for the component's lifetime (computed from `initial`
  // alone, never `voicesByTag`) — a tag stays pinned even if its own value is mid-edit.
  const pinnedTags = useMemo(
    () => new Set(initial?.voicePlan?.map((entry) => entry.tag) ?? []),
    [initial]
  );

  // F1 — a bed clear is only ever honest when there is nothing already committed to silently fail
  // to clear: creating a new spot, or editing a row that never had a bed in the first place. See
  // the class remarks' "Bed" bullet.
  const canClearBed = initial === null || initial.bedMediaId === null;

  function handleVoiceChange(tag: string, voiceId: string): void {
    if (voiceId === "" && pinnedTags.has(tag)) return; // F1 — pinned; the select snaps back.
    setVoicesByTag((prev) => {
      const next = { ...prev };
      if (voiceId === "") delete next[tag];
      else next[tag] = voiceId;
      return next;
    });
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();

    const trimmedBrand = brand.trim();
    const trimmedTitle = title.trim();
    if (trimmedBrand === "" || trimmedTitle === "") {
      setStatus({ kind: "error", detail: "Brand and title are both required." });
      return;
    }

    // F1 — refuse to submit a silent no-op clear: PATCH reads a null brief/script as "leave
    // unchanged" (AdSpotEdit's sparse contract), so emptying a field that started non-null and
    // submitting would leave the operator believing it cleared when nothing changed server-side.
    if (initial !== null && initial.brief !== null && brief.trim() === "") {
      setStatus({
        kind: "error",
        detail: "The brief can't be cleared once set — leave the existing text, or replace it with new text.",
        field: "brief",
      });
      return;
    }
    if (initial !== null && initial.script !== null && script.trim() === "") {
      setStatus({
        kind: "error",
        detail: "The script can't be cleared once set — leave the existing text, or replace it with new text.",
        field: "script",
      });
      return;
    }

    setStatus({ kind: "pending" });

    // Only a tag the operator picked an EXPLICIT voice for rides the plan — a tag left at "Station
    // default" is simply omitted (never a blank voiceId, which the server refuses outright:
    // AdsController.ValidateVoicePlanEntries) and falls back to the station voice at render time
    // (AdRenderService.ParseVoicePlan's own degrade). `flatMap` over a local, already-narrowed
    // `voiceId` (not `tags.filter(...).map(...)` re-reading the record and casting) — no `as
    // string` anywhere in this file.
    const voicePlan: AdVoicePlanEntry[] = tags.flatMap((tag) => {
      const voiceId = voicesByTag[tag];
      return voiceId !== undefined && voiceId !== "" ? [{ tag, voiceId, pace: 1.0 }] : [];
    });

    const body: AdSpotSaveBody = {
      brand: trimmedBrand,
      title: trimmedTitle,
      brief: brief.trim() === "" ? null : brief.trim(),
      script: script.trim() === "" ? null : script,
      voicePlan: voicePlan.length > 0 ? voicePlan : null,
      spotSeconds,
      bedMediaId: bed?.mediaId ?? null,
    };

    const outcome =
      initial === null ? await createAdSpot(body) : await updateAdSpot(initial.id, initial.version, body);

    if (!outcome.ok) {
      setStatus({ kind: "error", detail: outcome.detail, field: outcome.field, ruleId: outcome.ruleId });
      return;
    }
    setStatus({ kind: "idle" });
    onSaved(outcome.spot);
  }

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onCancel();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none" />
        <Dialog.Content
          aria-label={initial === null ? "New spot" : "Edit spot"}
          className="fixed left-1/2 top-1/2 z-50 flex max-h-[85vh] w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 flex-col overflow-y-auto rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={restoreFocus.onCloseAutoFocus}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">
            {initial === null ? "New spot" : "Edit spot"}
          </Dialog.Title>

          {status.kind === "error" && status.field !== "script" && (
            <p role="alert" aria-live="assertive" className="mt-3 text-[0.82rem] text-danger">
              {status.detail}
              {status.ruleId !== undefined && ` (rule: ${status.ruleId})`}
            </p>
          )}

          <form
            onSubmit={(e) => {
              void handleSubmit(e);
            }}
            className="mt-4 flex flex-col gap-4"
          >
            <FieldRow label="Brand" htmlFor="ad-brand">
              <input
                id="ad-brand"
                value={brand}
                onChange={(e) => setBrand(e.currentTarget.value)}
                disabled={isPending}
                className={FIELD_INPUT_CLASSES}
              />
            </FieldRow>

            <FieldRow label="Title" htmlFor="ad-title">
              <input
                id="ad-title"
                value={title}
                onChange={(e) => setTitle(e.currentTarget.value)}
                disabled={isPending}
                className={FIELD_INPUT_CLASSES}
              />
            </FieldRow>

            <FieldRow label="Brief" htmlFor="ad-brief">
              <textarea
                id="ad-brief"
                rows={2}
                value={brief}
                onChange={(e) => setBrief(e.currentTarget.value)}
                disabled={isPending}
                placeholder="Premise, tone, structure — a writing hint, never itself airable."
                className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
              />
            </FieldRow>

            <FieldRow label="Script" htmlFor="ad-script">
              <textarea
                id="ad-script"
                rows={6}
                value={script}
                onChange={(e) => setScript(e.currentTarget.value)}
                disabled={isPending}
                placeholder={"ANNOUNCER: ..."}
                className={`${FIELD_INPUT_CLASSES} resize-y py-2 font-mono`}
              />
              {status.kind === "error" && status.field === "script" && (
                <p role="alert" aria-live="assertive" className="text-[0.78rem] text-danger">
                  {status.detail}
                  {status.ruleId !== undefined && ` (rule: ${status.ruleId})`}
                </p>
              )}
            </FieldRow>

            {tags.length > 0 && (
              <div className="flex flex-col gap-3">
                <p className={FIELD_LABEL_CLASSES}>Voice cast</p>
                {tags.map((tag) => (
                  <div key={tag} className="flex flex-col gap-1">
                    <span className="text-[0.78rem] font-semibold text-accent-2">{tag}</span>
                    <VoiceControl
                      id={`ad-voice-${tag}`}
                      value={voicesByTag[tag] ?? ""}
                      onChange={(voiceId) => handleVoiceChange(tag, voiceId)}
                      disabled={isPending}
                    />
                    {pinnedTags.has(tag) && (
                      <p className="text-[0.72rem] text-mute">
                        Already cast — pick a different voice; it can&apos;t be reset to Station default
                        while editing.
                      </p>
                    )}
                  </div>
                ))}
              </div>
            )}

            <FieldRow label="Length" htmlFor="ad-seconds">
              <select
                id="ad-seconds"
                value={spotSeconds}
                onChange={(e) => setSpotSeconds(Number(e.currentTarget.value))}
                disabled={isPending}
                className={`${FIELD_INPUT_CLASSES} w-fit`}
              >
                {SPOT_SECONDS_OPTIONS.map((seconds) => (
                  <option key={seconds} value={seconds}>
                    {seconds}s
                  </option>
                ))}
              </select>
            </FieldRow>

            <BedPicker
              selected={bed}
              onSelect={setBed}
              onClear={() => setBed(null)}
              disabled={isPending}
              allowClear={canClearBed}
            />

            <div className="mt-2 flex justify-end gap-2">
              <Button type="button" variant="secondary" onClick={onCancel} disabled={isPending}>
                Cancel
              </Button>
              <Button type="submit" disabled={isPending}>
                {isPending ? "Saving…" : "Save"}
              </Button>
            </div>
          </form>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
