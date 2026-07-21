"use client";

import { useState, type ComponentType, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { cn } from "@/lib/utils";
import type { LibraryDto } from "@/lib/library";
import { CorrectionsSettingControl } from "./CorrectionsSettingControl";
import { PersonaSettingControl } from "./PersonaSettingControl";
import { SafeScopeAvailabilityBadge } from "./SafeScopeAvailabilityBadge";
import type { SettingsHelpKey } from "./settings-help-keys";
import { groupSettingsBySection } from "./settings-sections";
import type { SettingControlProps, SettingDto } from "./settings-types";
import { VoiceSettingControl } from "./VoiceSettingControl";

export type { SettingDto } from "./settings-types";

/** Shape of ASP.NET Core ValidationProblemDetails returned on 400. */
interface ValidationProblemDetails {
  errors: Record<string, string[]>;
  title?: string;
  status?: number;
}

interface SettingsFormProps {
  settings: SettingDto[];
  /**
   * Library rows used to populate the multi-select picker for `kind === "number-list"` settings.
   * Fetched server-side from GET /api/libraries and passed in by the parent server component.
   * Defaults to [] when not provided so existing tests and plain number settings are unaffected.
   */
  libraries?: LibraryDto[];
}

type SaveStatus = { kind: "idle" } | { kind: "saving" } | { kind: "noChanges" };

const SAFE_SCOPE_KEY = "Station:SafeScope:LibraryIds";
const MAIN_SCOPE_KEY = "Station:Scope:LibraryIds";
const RECENT_WINDOW_KEY = "Station:Rotation:RecentWindow";
const ARTIST_SEPARATION_KEY = "Station:Rotation:ArtistSeparation";
const EMPTY_MAIN_SCOPE_ERROR =
  "Main rotation scope cannot be empty — the station would go silent.";
const SAFE_SCOPE_EMPTY_CONFIRM_TITLE = "Save empty Safe scope";
const SAFE_SCOPE_EMPTY_CONFIRM_CONSEQUENCE =
  "Saving an empty SafeScope silences the stream on drain — mksafe emits silence until re-pointed.";

/**
 * Empty-selection behavior for a `number-list` field, keyed by setting key.
 *
 * SafeScope (K5, SPEC F21.5; modal per F28.9) treats an empty selection as a
 * legitimate — if degraded — state and asks for explicit operator consent via the
 * shared `useConfirm()` dialog before submitting it. Main scope (SPEC F23.5) treats
 * it as invalid input: an empty main scope is a silent station, so it is blocked
 * inline and never reaches the PUT.
 */
type EmptyListPolicy =
  | { readonly kind: "confirm"; readonly title: string; readonly consequence: string }
  | { readonly kind: "block"; readonly message: string };

const EMPTY_LIST_POLICIES: Record<string, EmptyListPolicy> = {
  [SAFE_SCOPE_KEY]: {
    kind: "confirm",
    title: SAFE_SCOPE_EMPTY_CONFIRM_TITLE,
    consequence: SAFE_SCOPE_EMPTY_CONFIRM_CONSEQUENCE,
  },
  [MAIN_SCOPE_KEY]: { kind: "block", message: EMPTY_MAIN_SCOPE_ERROR },
};

/**
 * Per-field helper copy keyed by setting key, rendered under the input alongside the unit/badges
 * (SPEC F42.2, STORY-136). A small additive lookup — same shape as {@link EMPTY_LIST_POLICIES}
 * above — rather than a new metadata layer.
 *
 * SPEC F55.3 (closes gitea-#230, gitea-#231) grows this map from 3 entries to full allowlist coverage — every
 * key in {@link SETTINGS_HELP_KEYS} (mirroring `StationSettingsAllowlist.All`) has one
 * plain-language sentence: what the knob does and, for the F53-ceilinged keys, its accepted range
 * (copy matches `SettingValidator`'s actual bounds — see `settings-help-coverage.spec.tsx`'s
 * parity guard, which fails the build if a future allowlist key lands here without an entry).
 */
const FIELD_HELP_TEXT: Record<SettingsHelpKey, string> = {
  // ── Loudness (both bounds pre-exist F53; already documented as "carrying both bounds") ──────
  "Loudness:TargetLufs":
    "The loudness target the mix is normalized toward, in LUFS — every track is gained to match " +
    "it before crossfading. Accepted range: -40 to 0.",
  "Loudness:CeilingDbtp":
    "The true-peak ceiling the mix must not exceed, in dBTP — guards against inter-sample " +
    "clipping downstream. Accepted range: -12 to 0.",

  // ── Station identity (SPEC F44.1, F44.2, F44.5) ───────────────────────────────────────────────
  // Station:Name badges live (the api-side effects are genuinely immediate) but the Icecast
  // stream/directory name is read by the engine from STATION_NAME at container start, so this one
  // field carries an engine-restart caveat alongside its live badge.
  "Station:Name":
    "The public Icecast stream/directory name updates on the next engine restart; patter, " +
    "metadata, and this console update immediately.",
  "Station:Voice":
    "The Kokoro voice used for station-branded patter (station IDs, time/date, lead-ins, " +
    "back-announces) whenever no persona is active.",

  // ── Cadence ────────────────────────────────────────────────────────────────────────────────
  "Station:Cadence:LeadInBeforeEachTrack":
    "When on, a short spoken lead-in airs immediately before each track begins.",
  "Station:Cadence:BackAnnounceAfterEachTrack":
    "When on, a short spoken back-announce airs immediately after each track ends.",
  "Station:Cadence:StationIdEveryNUnits":
    "0 disables station IDs entirely — no station ID ever airs. Accepted range: 0–1000.",

  // ── Scope ──────────────────────────────────────────────────────────────────────────────────
  "Station:Scope:LibraryIds":
    "The libraries the main rotation picks tracks from — must name at least one library, or the " +
    "station has nothing to play.",
  "Station:SafeScope:LibraryIds":
    "The libraries used for safe filler content when the main rotation drains — an empty list is " +
    "legal and falls back to silence (mksafe).",
  "Station:Persona:ActiveId":
    "The currently active DJ persona, if any — 0 means no persona; patter falls back to the " +
    "station-branded voice and copy.",

  // ── Rotation resilience + artist separation (SPEC F41.6, F53.1, F56.1, closes gitea-#210/gitea-#213/gitea-#227) ─
  "Station:Rotation:RecentWindow":
    "How many recently-played tracks the rotation avoids repeating. 0 disables anti-repeat " +
    "entirely. Accepted range: 0–10000.",
  "Station:Rotation:ArtistSeparation":
    "How many of the recent tracks must pass before the same artist can repeat. Separation is " +
    "limited by RecentWindow — it reads the same recent-tracks window, so a value deeper than " +
    "RecentWindow has no additional effect, and RecentWindow=0 disables artist separation too. " +
    "Accepted range: 0–100.",

  // ── Spectator surface (SPEC F62.1, F62.8, STORY-167/170) ─────────────────────────────────────
  "Station:SpectatorMode":
    "Public read-only spectator page and API. Off by default; when on, /spectator serves " +
    "without login.",
  "Station:PublicStreamUrl":
    "Public stream URL for the spectator page player (e.g. /stream behind the reference proxy). " +
    "Empty hides the player.",

  // ── TTS / LLM endpoints (SPEC F36.1–F36.4) ─────────────────────────────────────────────────
  "Tts:Endpoint":
    "The Kokoro TTS service base URL used to render all spoken patter — must be a non-empty " +
    "absolute http/https URL; there is no \"disabled TTS\" state.",
  "Tts:Corrections":
    "Operator pronunciation corrections applied to every spoken line before it reaches Kokoro " +
    "(e.g. \"MacLeod\" → \"Muh-cloud\"). A JSON array of {from, to} pairs; empty means no " +
    "corrections.",
  "Tts:Fallback:Endpoint":
    "The Piper local fallback TTS service base URL — used automatically when Kokoro is " +
    "unhealthy or a render fails. Leave empty to disable the fallback engine entirely.",
  "Tts:Fallback:Voice":
    "The Piper voice model the fallback service is expected to be running — informational " +
    "only; it is not sent with each render and does not change which voice Piper speaks with.",
  "Tts:EngineByKind":
    "Pins specific speech kinds to a specific engine, e.g. {\"StationId\":\"piper\"} so a short " +
    "ident always uses the cheap voice. A JSON object mapping StationId/LeadIn/BackAnnounce/" +
    "TimeDate to \"kokoro\" or \"piper\"; empty means every kind uses the normal Kokoro-first, " +
    "Piper-fallback routing.",
  "Llm:Endpoint":
    "The LLM completion service base URL used to author patter copy — leave empty to disable " +
    "LLM-authored copy and fall back to templated copy.",
  "Llm:Model":
    "The model name requested from the LLM endpoint — free text, meaningful only when " +
    "Llm:Endpoint is set.",
  "Llm:TimeoutSeconds":
    "How long an LLM completion request is allowed to run before falling back to templated " +
    "copy. Accepted range: 1–300.",

  // ── F44.2 allowlist completion (closes gitea-#197) ───────────────────────────────────────────────
  "Tts:RenderBudgetSeconds":
    "How long a single TTS render is allowed to take before it is abandoned. Accepted range: " +
    "1–600.",
  "Tts:BlurbRetentionHours":
    "How long a rendered TTS blurb stays cached before the garbage-collection sweep removes it. " +
    "Accepted range: 1–8760.",
  "Llm:MaxCopyChars":
    "The maximum length, in characters, of LLM-authored patter copy — longer completions are " +
    "truncated. Accepted range: 1–10000.",
  "Admin:PlayHistoryCapacity":
    "How many recent plays the play-history ring keeps before the oldest entry is dropped. " +
    "Accepted range: 1–5000.",
  "Library:ScanIntervalSeconds":
    "How often the library scans for new or changed files, in seconds. Accepted range: 1–86400.",
  "Library:EnrichmentConcurrency":
    "How many tracks are analyzed (loudness, cue points, energy, tags) in parallel during " +
    "enrichment. Accepted range: 1–32.",

  // ── Scan availability grace (SPEC F58.3, closes gitea-#223) ──────────────────────────────────────
  "Library:Scan:MissThreshold":
    "How many consecutive scan ticks a file may be missing from the library folder before it is " +
    "marked unavailable — a higher value rides out a brief NFS/mount blip without pulling a " +
    "track from rotation. Accepted range: 1–20.",

  // ── MusicBrainz year lookup (SPEC F48.5, F55.1, F55.2, closes gitea-#208/gitea-#230) ───────────────────
  // F55.2 exact reword (closes gitea-#230) — verbatim per SPEC.
  "Library:YearLookup:Enabled":
    "When on, tracks missing a release year get one looked up from MusicBrainz during " +
    "enrichment. Turning it off stops future lookups; years already filled stay.",
  "Library:YearLookup:Endpoint":
    "The MusicBrainz web service base URL used to look up a missing release year — must be a " +
    "non-empty absolute http/https URL.",
  "Library:YearLookup:MinScore":
    "The minimum MusicBrainz match confidence (0-100) a candidate must reach before its year is " +
    "accepted. Accepted range: 0–100.",

  // ── Engine-restart knobs ────────────────────────────────────────────────────────────────────
  "GW_XFADE_MIN":
    "The shortest crossfade duration the engine uses between tracks, in seconds. Must be " +
    "greater than 0, at most 30.",
  "GW_XFADE_MAX":
    "The longest crossfade duration the engine uses between tracks, in seconds. Must be " +
    "greater than 0, at most 30.",
  "GW_SAFE_GAP_SECONDS":
    "The silence gap the engine inserts between consecutive safe-rotation tracks, in seconds. " +
    "0 disables the gap. Accepted range: 0–600.",

  // ── Enrichment-mode knobs (F44.3) ──────────────────────────────────────────────────────────
  "Library:CueDetection:MinSilenceDurationSec":
    "The shortest silent region, in seconds, that counts as a cue point during trim detection. " +
    "Applies the next time a file is (re-)analyzed. Must be greater than 0, at most 60.",
  "Library:Energy:WindowSeconds":
    "The length of the intro/outro window measured for energy analysis, in seconds. Applies " +
    "the next time a file is (re-)analyzed. Must be greater than 0, at most 60.",

  // ── LLM degradation (SPEC F69.3) ───────────────────────────────────────────────────────────
  "Llm:DegradationPin":
    "Pins the LLM degradation mode instead of letting it auto-adjust to failures/recoveries. " +
    "\"auto\" (default) follows automatically; \"normal\", \"soft\", or \"hard\" holds that mode " +
    "until this is set back to \"auto\".",
};

/**
 * Looks up help copy for an arbitrary rendered key. `FIELD_HELP_TEXT` is typed as
 * `Record<SettingsHelpKey, string>` so the compiler itself enforces full allowlist coverage
 * (an entry missing or misspelled fails `tsc`, not just a spec) — but `setting.key` at render
 * time is a plain `string`, so the lookup widens the record's key type to look it up safely.
 * Widening the KEY type this way is not a value lie: it never lets a wrong VALUE through, it
 * only relaxes what strings may be used to look one up, and still returns `undefined` for any
 * key `FIELD_HELP_TEXT` doesn't carry.
 */
function helpTextFor(key: string): string | undefined {
  return (FIELD_HELP_TEXT as Record<string, string | undefined>)[key];
}

/**
 * Per-key control-override registry (SPEC F54.1) — the `FIELD_HELP_TEXT` precedent applied to
 * whole controls instead of a help sentence. A key present here renders its registered component
 * in place of `SettingField`'s kind-based chain; keys absent keep that shipped rendering
 * unchanged. Zero API/wire changes: this only decides *which control* renders, never what gets
 * submitted — every registered control still lands its value in the same `values` map and rides
 * the same changed-keys PUT batch as every kind-based branch (F54.4).
 */
const SETTING_CONTROL_REGISTRY: Record<string, ComponentType<SettingControlProps>> = {
  "Station:Voice": VoiceSettingControl,
  "Station:Persona:ActiveId": PersonaSettingControl,
  "Tts:Corrections": CorrectionsSettingControl,
};

/** applyMode badge copy (SPEC F28.12 wording verbatim; F44.3 adds the third "enrichment" mode). */
function applyModeLabel(mode: SettingDto["applyMode"]): string {
  switch (mode) {
    case "live":
      return "live";
    case "enrichment":
      return "applies at next enrichment";
    default:
      return "applies after engine restart";
  }
}

function sourceLabel(source: SettingDto["source"]): string {
  return source === "override" ? "override" : "default";
}

function isValidationProblemDetails(raw: unknown): raw is ValidationProblemDetails {
  if (typeof raw !== "object" || raw === null) return false;
  const obj = raw as Record<string, unknown>;
  return typeof obj["errors"] === "object" && obj["errors"] !== null;
}

/** Build the initial values map from the loaded settings. */
function initialValuesFrom(settings: SettingDto[]): Record<string, string> {
  return Object.fromEntries(settings.map((s) => [s.key, s.value]));
}

/** Diff current values against original; return only entries that changed. */
function changedEntries(
  original: Record<string, string>,
  current: Record<string, string>
): Array<{ key: string; value: string }> {
  return Object.entries(current)
    .filter(([key, value]) => value !== original[key])
    .map(([key, value]) => ({ key, value }));
}

/** Parse a JSON array-string like "[1,2]" into an array of numbers. Returns [] on any error. */
function parseLibraryIds(value: string): number[] {
  if (value === "") return [];
  try {
    const parsed: unknown = JSON.parse(value);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((x): x is number => typeof x === "number");
  } catch {
    return [];
  }
}

/** True when the given number-list value encodes an empty selection ([] or unset). */
function isEmptyList(value: string): boolean {
  return parseLibraryIds(value).length === 0;
}

/**
 * True when a boolean-kind setting's staged value means "on". Case-insensitive: the .NET JSON
 * configuration provider surfaces an appsettings.json `true` literal as the string `"True"`
 * (capital T) — a case-sensitive `=== "true"` check therefore renders every appsettings-sourced
 * boolean (Library:YearLookup:Enabled, both Station:Cadence:* toggles) as unchecked while the
 * knob is actually on. The write path is unaffected: {@link SettingsForm}'s checkbox `onChange`
 * still emits lowercase `"true"`/`"false"` (matching what `PUT /api/settings` and
 * `SettingValidator`'s already-case-insensitive `bool.TryParse` expect), so this only widens what
 * counts as "checked" on render, never what gets submitted.
 */
function isCheckedBooleanValue(value: string): boolean {
  return value.trim().toLowerCase() === "true";
}

/**
 * Parses a rotation-knob field's staged text value into a finite number, or `null` when the
 * field is absent from the form (not every settings fixture carries both rotation keys) or the
 * staged text doesn't parse — either way, "unknown" rather than "0" so the F56.2 notice below
 * never fires off a guess.
 */
function parseRotationCount(raw: string | undefined): number | null {
  if (raw === undefined || raw === "") return null;
  const parsed = Number(raw);
  return Number.isFinite(parsed) ? parsed : null;
}

export function SettingsForm({ settings, libraries = [] }: SettingsFormProps): ReactNode {
  const confirm = useConfirm();
  const [original] = useState<Record<string, string>>(() => initialValuesFrom(settings));
  const [values, setValues] = useState<Record<string, string>>(() => initialValuesFrom(settings));
  const [status, setStatus] = useState<SaveStatus>({ kind: "idle" });
  /**
   * Per-field validation errors surfaced inline next to the relevant control.
   * Populated when a 400 is returned from PUT — attributed to every key in the
   * submitted batch, since the backend reports validation failures batch-wide
   * under a single "settings" key (F28.9: field-level errors stay inline, never
   * a page-wide banner).
   */
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});

  function handleTextChange(key: string): (e: React.ChangeEvent<HTMLInputElement>) => void {
    return (e) => {
      const val = e.currentTarget.value;
      setValues((prev) => ({ ...prev, [key]: val }));
    };
  }

  function handleCheckboxChange(key: string): (e: React.ChangeEvent<HTMLInputElement>) => void {
    return (e) => {
      const val = e.currentTarget.checked ? "true" : "false";
      setValues((prev) => ({ ...prev, [key]: val }));
    };
  }

  /**
   * Handler for registered controls (SPEC F54.1) — these components hand back a plain value
   * rather than a DOM change event (the safe-content `VoiceControl` precedent's `onChange`
   * shape), so this is a value-in, not event-in, sibling of {@link handleTextChange}.
   */
  function handleSemanticChange(key: string): (value: string) => void {
    return (val) => {
      setValues((prev) => ({ ...prev, [key]: val }));
    };
  }

  /**
   * Handler for the multi-select library picker. Reads the selected option values (library IDs),
   * encodes them as a JSON array string, and stores them in `values` under the setting key.
   * This matches the wire format expected by PUT /api/settings for NumberList kind.
   */
  function handleMultiSelectChange(key: string): (e: React.ChangeEvent<HTMLSelectElement>) => void {
    return (e) => {
      const selectedIds = Array.from(e.currentTarget.selectedOptions)
        .map((opt) => parseInt(opt.value, 10))
        .filter((id) => !isNaN(id));
      setValues((prev) => ({ ...prev, [key]: JSON.stringify(selectedIds) }));

      // Fields with a "block" empty-list policy (e.g. main scope) surface their inline
      // error as soon as the operator empties the selection — not only on submit — so
      // Save is visibly blocked before the operator even reaches for the button.
      const policy = EMPTY_LIST_POLICIES[key];
      if (selectedIds.length === 0 && policy?.kind === "block") {
        setFieldErrors((prev) => ({ ...prev, [key]: [policy.message] }));
        return;
      }

      // Clear any prior field-level error for this key when the user makes a change
      if (fieldErrors[key] !== undefined) {
        setFieldErrors((prev) => {
          const next = { ...prev };
          delete next[key];
          return next;
        });
      }
    };
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();

    const changed = changedEntries(original, values);
    if (changed.length === 0) {
      setStatus({ kind: "noChanges" });
      return;
    }

    // Changed number-list fields whose new value is an empty selection — each such field
    // carries its own empty-list policy (SafeScope confirms via modal, main scope blocks;
    // see EMPTY_LIST_POLICIES above).
    const emptyListChanges = changed.filter(
      (c) => isEmptyList(c.value) && EMPTY_LIST_POLICIES[c.key] !== undefined
    );

    // SPEC F23.5 — a field with a "block" policy (main scope) never reaches the PUT: the
    // inline error is (re)asserted and submission stops here, with no confirm dialog.
    const blockingChange = emptyListChanges.find((c) => EMPTY_LIST_POLICIES[c.key]?.kind === "block");
    if (blockingChange !== undefined) {
      const policy = EMPTY_LIST_POLICIES[blockingChange.key];
      setFieldErrors((prev) => ({
        ...prev,
        [blockingChange.key]: [policy?.kind === "block" ? policy.message : ""],
      }));
      setStatus({ kind: "idle" });
      return;
    }

    // SPEC F21.5/F28.9 — clearing all libraries from a "confirm" field (SafeScope) requires
    // explicit operator consent, via the shared useConfirm() modal, before the PUT is sent —
    // an empty scope means silence on stream drain. Cancel leaves the staged value unsaved;
    // confirm submits [] like any other changed field.
    const confirmChange = emptyListChanges.find((c) => EMPTY_LIST_POLICIES[c.key]?.kind === "confirm");
    if (confirmChange !== undefined) {
      const policy = EMPTY_LIST_POLICIES[confirmChange.key];
      if (policy?.kind === "confirm") {
        const confirmed = await confirm({
          title: policy.title,
          consequence: policy.consequence,
          confirmLabel: "Save",
          destructive: true,
        });
        if (!confirmed) {
          setStatus({ kind: "idle" });
          return;
        }
      }
    }

    setStatus({ kind: "saving" });
    setFieldErrors({});

    try {
      const resp = await fetch("/api/settings", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(changed),
      });

      if (resp.ok) {
        setStatus({ kind: "idle" });
        toast.success("Settings saved.");
        return;
      }

      if (resp.status === 400) {
        let messages: string[] = [];
        try {
          const raw = (await resp.json()) as unknown;
          if (isValidationProblemDetails(raw)) {
            const errors = raw.errors as Record<string, string[]>;
            const settingsErrors = errors["settings"];
            if (Array.isArray(settingsErrors) && settingsErrors.length > 0) {
              messages = settingsErrors;
            } else {
              messages = Object.values(errors).flat();
            }
          }
        } catch {
          // malformed 400 body — fall through to empty messages
        }

        // AC5 — every key in the submitted batch gets the returned message(s) inline at its
        // field (the backend reports validation failures batch-wide, not per key). Status
        // resets to idle so isPending drops to false and the form re-enables, letting the
        // operator correct the value and retry (the K5 stuck-Saving regression class).
        setStatus({ kind: "idle" });
        setFieldErrors(Object.fromEntries(changed.map((c) => [c.key, messages])));
        return;
      }

      setStatus({ kind: "idle" });
      toast.error(`Unexpected error (${resp.status})`);
    } catch {
      setStatus({ kind: "idle" });
      toast.error("Network error — check your connection");
    }
  }

  const isPending = status.kind === "saving";

  /**
   * SPEC F25.4 — Derives whether the SafeScope effective value (from the GET response,
   * i.e. the original settings prop) is an empty list. Uses the prop directly so the
   * badge tracks the bound value, not the operator's staged (unsubmitted) selection.
   */
  const safeScopeEffectivelyEmpty = isEmptyList(
    settings.find((s) => s.key === SAFE_SCOPE_KEY)?.value ?? ""
  );

  /**
   * SPEC F56.2 (closes gitea-#227) — computed from the form's CURRENT (pre-submit) staged values, not
   * the persisted `settings` prop, so an in-progress edit surfaces the notice immediately and
   * clearing it back below the threshold hides it again, all before Save is ever pressed. Reads
   * `values` (not `original`) deliberately. Both operands come back `null` when either rotation
   * key isn't on this settings page or hasn't parsed yet, so the notice never renders off a guess.
   */
  const recentWindowValue = parseRotationCount(values[RECENT_WINDOW_KEY]);
  const artistSeparationValue = parseRotationCount(values[ARTIST_SEPARATION_KEY]);
  const rotationCouplingNotice: ReactNode =
    recentWindowValue !== null &&
    artistSeparationValue !== null &&
    artistSeparationValue > recentWindowValue ? (
      <RotationCouplingNotice recentWindow={recentWindowValue} />
    ) : null;

  const sections = groupSettingsBySection(settings);

  return (
    <form onSubmit={(e) => { void handleSubmit(e); }} className="flex flex-col gap-6">
      {status.kind === "noChanges" && (
        <p role="status" aria-live="polite" className="text-[0.85rem] text-mute">
          No changes to save.
        </p>
      )}

      {sections.map((section) => (
        <SectionCard key={section.id} title={section.label}>
          {section.settings.map((setting) => (
            <SettingField
              key={setting.key}
              setting={setting}
              value={values[setting.key] ?? ""}
              errors={fieldErrors[setting.key] ?? []}
              isPending={isPending}
              libraries={libraries}
              isSafeScopeField={setting.key === SAFE_SCOPE_KEY}
              safeScopeEffectivelyEmpty={safeScopeEffectivelyEmpty}
              rotationCouplingNotice={setting.key === ARTIST_SEPARATION_KEY ? rotationCouplingNotice : null}
              onTextChange={handleTextChange(setting.key)}
              onCheckboxChange={handleCheckboxChange(setting.key)}
              onMultiSelectChange={handleMultiSelectChange(setting.key)}
              onSemanticChange={handleSemanticChange(setting.key)}
            />
          ))}
        </SectionCard>
      ))}

      <Button type="submit" disabled={isPending} className="self-start">
        {isPending ? "Saving…" : "Save settings"}
      </Button>
    </form>
  );
}

// ---------------------------------------------------------------------------
// Section / field presentation (SPEC F28.12, .claude/skills/design-aesthetic)
// ---------------------------------------------------------------------------

interface SectionCardProps {
  title: string;
  children: ReactNode;
}

function SectionCard({ title, children }: SectionCardProps): ReactNode {
  return (
    <section aria-label={title} className="rounded-[6px] border border-line bg-surface p-5">
      <h2 className="font-display text-[1.1rem] text-ink">{title}</h2>
      <div className="mt-4 flex flex-col gap-5">{children}</div>
    </section>
  );
}

interface SettingFieldProps {
  setting: SettingDto;
  value: string;
  errors: string[];
  isPending: boolean;
  libraries: LibraryDto[];
  /** True for the single SafeScope field — the only field that mounts {@link SafeScopeAvailabilityBadge}. */
  isSafeScopeField: boolean;
  /** SPEC F25.4's effective-empty signal, threaded through to {@link SafeScopeAvailabilityBadge} for badge precedence. */
  safeScopeEffectivelyEmpty: boolean;
  /**
   * SPEC F56.2's non-blocking coupling notice, pre-built by {@link SettingsForm} (which alone
   * knows both rotation keys' current staged values) — `null` for every field except
   * ArtistSeparation, and `null` there too unless the current values are actually capped.
   */
  rotationCouplingNotice: ReactNode;
  onTextChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  onCheckboxChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  onMultiSelectChange: (e: React.ChangeEvent<HTMLSelectElement>) => void;
  /** Feeds a registered control (SPEC F54.1) — see {@link SETTING_CONTROL_REGISTRY}. */
  onSemanticChange: (value: string) => void;
}

function SettingField({
  setting,
  value,
  errors,
  isPending,
  libraries,
  isSafeScopeField,
  safeScopeEffectivelyEmpty,
  rotationCouplingNotice,
  onTextChange,
  onCheckboxChange,
  onMultiSelectChange,
  onSemanticChange,
}: SettingFieldProps): ReactNode {
  const controlId = `setting-${setting.key}`;
  const RegisteredControl = SETTING_CONTROL_REGISTRY[setting.key];
  const helpText = helpTextFor(setting.key);

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <label htmlFor={controlId} className="text-[0.85rem] font-semibold text-ink">
          {setting.key}
          {setting.unit !== "" && (
            <span aria-label={`Unit: ${setting.unit}`} className="ml-1 font-normal text-mute">
              ({setting.unit})
            </span>
          )}
        </label>
        <div className="flex items-center gap-1.5">
          <ApplyModeBadge mode={setting.applyMode} />
          <SourceChip source={setting.source} />
        </div>
      </div>

      {RegisteredControl !== undefined ? (
        <RegisteredControl
          controlId={controlId}
          value={value}
          onChange={onSemanticChange}
          disabled={isPending}
        />
      ) : setting.kind === "boolean" ? (
        <span className="flex min-h-10 items-center self-start">
          <input
            id={controlId}
            name={setting.key}
            type="checkbox"
            checked={isCheckedBooleanValue(value)}
            onChange={onCheckboxChange}
            disabled={isPending}
            className="h-4 w-4 disabled:opacity-50"
          />
        </span>
      ) : setting.kind === "number-list" ? (
        <select
          id={controlId}
          name={setting.key}
          multiple
          value={parseLibraryIds(value).map(String)}
          onChange={onMultiSelectChange}
          disabled={isPending}
          className="min-h-24 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 py-1 text-[0.85rem] text-ink disabled:opacity-50"
        >
          {libraries.map((lib) => (
            <option key={lib.id} value={String(lib.id)}>
              {lib.name}
            </option>
          ))}
        </select>
      ) : setting.kind === "string" ? (
        <input
          id={controlId}
          name={setting.key}
          type="text"
          value={value}
          onChange={onTextChange}
          disabled={isPending}
          className="h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50"
        />
      ) : (
        <input
          id={controlId}
          name={setting.key}
          type="number"
          value={value}
          onChange={onTextChange}
          disabled={isPending}
          className="h-9 max-w-xs rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink tabular-nums disabled:opacity-50"
        />
      )}

      {isSafeScopeField && (
        <SafeScopeAvailabilityBadge effectivelyEmpty={safeScopeEffectivelyEmpty} />
      )}

      {helpText !== undefined && (
        <p data-testid={`setting-help-${setting.key}`} className="text-[0.78rem] text-mute">
          {helpText}
        </p>
      )}

      {rotationCouplingNotice}

      {errors.length > 0 && (
        <span role="alert" aria-live="assertive" className="text-[0.78rem] text-danger">
          {errors.join("; ")}
        </span>
      )}
    </div>
  );
}

/**
 * Pill badge (999px radius): quiet token treatment for "live", brass/warning treatment for
 * "engine restart". "enrichment" (SPEC F44.3) gets a third, distinct-but-quiet treatment — the
 * same quiet surface/text as live (never shouts like the engine-restart brass text), bordered in
 * brass to hint "this needs something to happen" without claiming the accent color reserved for
 * on-air/primary state.
 */
function ApplyModeBadge({ mode }: { mode: SettingDto["applyMode"] }): ReactNode {
  const styles =
    mode === "live"
      ? "border-line bg-surface-2 text-mute"
      : mode === "enrichment"
        ? "border-accent-2 bg-surface-2 text-mute"
        : "border-accent-2 bg-transparent text-accent-2";
  return (
    <span
      aria-label={`Apply mode: ${applyModeLabel(mode)}`}
      className={cn(
        "inline-flex items-center rounded-[999px] border px-2.5 py-1 text-[0.68rem] font-semibold uppercase tracking-[0.12em]",
        styles
      )}
    >
      {applyModeLabel(mode)}
    </span>
  );
}

/** 3px-radius bordered chip for the source tag, per design-aesthetic chip conventions. */
function SourceChip({ source }: { source: SettingDto["source"] }): ReactNode {
  return (
    <span
      aria-label={`Source: ${sourceLabel(source)}`}
      data-source={source}
      className="inline-flex items-center rounded-[3px] border border-line px-1.5 py-0.5 text-[0.68rem] text-mute"
    >
      [{sourceLabel(source)}]
    </span>
  );
}

interface RotationCouplingNoticeProps {
  /** The RecentWindow value ArtistSeparation is currently capped at. */
  recentWindow: number;
}

/**
 * SPEC F56.2 (closes gitea-#227) — non-blocking inline notice on the ArtistSeparation field: when the
 * form's CURRENT ArtistSeparation exceeds RecentWindow, the artist tier is silently capped at the
 * window size (F41.6/F56.3 — there is no separate artist memory; the artist tier reads the tail
 * of the same recent-tracks ring). This is a hint, not a validation error — quiet accent-2
 * treatment matching {@link SafeScopeAvailabilityBadge}'s non-error "Silent on drain" copy, never
 * `role="alert"`, and it never blocks `SettingsForm`'s submit path (F56.4: no server-side rule
 * exists for this shape, none is added).
 */
function RotationCouplingNotice({ recentWindow }: RotationCouplingNoticeProps): ReactNode {
  return (
    <p data-testid="rotation-coupling-notice" className="text-[0.78rem] text-accent-2">
      Capped by RecentWindow — effective separation is {recentWindow} track
      {recentWindow === 1 ? "" : "s"} until RecentWindow grows.
    </p>
  );
}
