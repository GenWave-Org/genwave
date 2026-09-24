"use client";

import {
  useEffect,
  useState,
  type ComponentType,
  type FormEvent,
  type ReactNode,
} from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { formatDateStamp } from "@/lib/format-clock";
import { cn } from "@/lib/utils";
import type { LibraryDto } from "@/lib/library";
import { AudienceSettingControl } from "./AudienceSettingControl";
import { ChoiceSettingControl } from "./ChoiceSettingControl";
import { CorrectionsSettingControl } from "./CorrectionsSettingControl";
import { EngineByKindSettingControl } from "./EngineByKindSettingControl";
import { PersonaSettingControl } from "./PersonaSettingControl";
import { SafeScopeAvailabilityBadge } from "./SafeScopeAvailabilityBadge";
import { SettingHelpFlyover } from "./SettingHelpFlyover";
import { groupSettingsBySection } from "./settings-sections";
import {
  isValidationProblemDetails,
  type SettingChoice,
  type SettingControlProps,
  type SettingDto,
} from "./settings-types";
import { VoiceSettingControl } from "./VoiceSettingControl";

export type { SettingDto } from "./settings-types";

interface SettingsFormProps {
  settings: SettingDto[];
  /**
   * Library rows used to populate the multi-select picker for `kind === "number-list"` settings.
   * Fetched server-side from GET /api/libraries and passed in by the parent server component.
   * Defaults to [] when not provided so existing tests and plain number settings are unaffected.
   */
  libraries?: LibraryDto[];
  /** Test-only injection point for the theme provenance list's `formatDateStamp` call (SPEC
   * F103.11, PLAN T187 review F2); production omits this and gets the browser's local zone — the
   * same StatusTiles/BoothLogFeed/LlmCallsFeed/`PersonasClient` idiom, not a bespoke one. */
  timeZone?: string;
  /**
   * Extra content mounted at the end of the page, after every section card (PLAN T145 review
   * F3) — the escape hatch for a dedicated-API surface (`PronunciationRulesControl`) whose
   * read/write shape cannot fit the per-key `SETTING_CONTROL_REGISTRY` (see that component's own
   * remarks: it reads a merged view no settings key carries and writes immediately, never through
   * the page-wide Save batch). `SettingsForm` stays ignorant of any specific dedicated-API
   * surface — `page.tsx` supplies the element — so a caller that omits this prop (every existing
   * spec) renders byte-identical to before, the same injection-point idiom as {@link timeZone}.
   */
  trailingContent?: ReactNode;
}

type SaveStatus = { kind: "idle" } | { kind: "saving" } | { kind: "noChanges" };

const SAFE_SCOPE_KEY = "Station:SafeScope:LibraryIds";
const MAIN_SCOPE_KEY = "Station:Scope:LibraryIds";
const RECENT_WINDOW_KEY = "Station:Rotation:RecentWindow";
const ARTIST_SEPARATION_KEY = "Station:Rotation:ArtistSeparation";
const THEME_KEY = "Station:Theme";

/**
 * Whole-document settings keys guarded by optimistic concurrency (gh-#486) — the two JSON-array
 * keys the issue named (`Tts:Pronunciations`'s own raw fallback field here; its dedicated
 * `/api/pronunciations` CRUD carries its own server-internal guard, see that controller's own
 * remarks). A changed entry for one of these keys carries `expectedVersion` on the PUT batch
 * (from the last GET/PUT response's own {@link SettingDto.version}); every other key is submitted
 * exactly as it always was.
 */
const VERSION_GUARDED_KEYS = new Set<string>(["Tts:Corrections", "Tts:Pronunciations"]);
const EMPTY_MAIN_SCOPE_ERROR =
  "Main rotation scope cannot be empty — the station would go silent.";
const SAFE_SCOPE_EMPTY_CONFIRM_TITLE = "Save empty Station sounds scope";
const SAFE_SCOPE_EMPTY_CONFIRM_CONSEQUENCE =
  "Saving an empty Station sounds scope (Station:SafeScope:LibraryIds) silences the stream on " +
  "drain — mksafe emits silence until re-pointed.";

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
 * Per-key control-override registry (SPEC F54.1) — the same additive-lookup shape as
 * {@link EMPTY_LIST_POLICIES} above, applied to whole controls instead of a help sentence. A key
 * present here renders its registered component
 * in place of `SettingField`'s kind-based chain; keys absent keep that shipped rendering
 * unchanged. Zero API/wire changes: this only decides *which control* renders, never what gets
 * submitted — every registered control still lands its value in the same `values` map and rides
 * the same changed-keys PUT batch as every kind-based branch (F54.4).
 */
const SETTING_CONTROL_REGISTRY: Record<string, ComponentType<SettingControlProps>> = {
  "Station:Voice": VoiceSettingControl,
  "Tts:Corrections": CorrectionsSettingControl,
  "Tts:EngineByKind": EngineByKindSettingControl,
  "Station:Audience": AudienceSettingControl,
  // T175 (SPEC F102.14, STORY-265) — ChoiceSettingControl is generic over `setting.choices`, not
  // Theme-specific; a future SettingKind.Choice setting registers the SAME component here.
  "Station:Theme": ChoiceSettingControl,
  // Station:IconPack (SPEC F130.4, STORY-337, PLAN T303) is deliberately NOT registered here
  // (review finding F1) — SettingField's own kind-chain fallback (`setting.kind === "choice"`)
  // already routes every unregistered Choice-kind setting to ChoiceSettingControl, the SAME
  // component a registry entry would point at; a second entry here would be a redundant path to
  // the identical component, not a different behavior. This registry stays reserved for a KEY
  // that needs something OTHER than the generic Choice control.
  // gh-#426 — both hold a persona ROW ID; PersonaSettingControl is generic over which key it's
  // fed (it never hardcodes either), so one component serves both registrations.
  "Context:Weather:PersonaId": PersonaSettingControl,
  "Context:History:PersonaId": PersonaSettingControl,
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

/** Build the initial values map from the loaded settings. */
function initialValuesFrom(settings: SettingDto[]): Record<string, string> {
  return Object.fromEntries(settings.map((s) => [s.key, s.value]));
}

/** Build the initial version-token map from the loaded settings (gh-#486) — `undefined` for a
 * fixture/response that predates the field, so a guarded key with no known version simply skips
 * the guard on its next save rather than throwing or fabricating a version. */
function initialVersionsFrom(settings: SettingDto[]): Record<string, number | undefined> {
  return Object.fromEntries(settings.map((s) => [s.key, s.version]));
}

/**
 * Re-fetches the CURRENT settings (gh-#486's 409 recovery path: refetch and tell the operator
 * their view was stale, never silently merge) — same shape/endpoint `page.tsx`'s own server-side
 * load reads, called client-side here since this form only ever runs after that initial load.
 * `null` on any failure — the 409 handler falls back to clearing the just-submitted keys'
 * versions instead (the same "lose the guard, not the operator's other edits" degrade
 * {@link handleSubmit}'s own malformed-PUT-response branch uses).
 */
async function fetchSettingsSnapshot(): Promise<SettingDto[] | null> {
  try {
    const resp = await fetch("/api/settings", { credentials: "include", cache: "no-store" });
    if (!resp.ok) return null;
    const raw: unknown = await resp.json();
    return Array.isArray(raw) ? (raw as SettingDto[]) : null;
  } catch {
    return null;
  }
}

/**
 * Diff current values against original; return only entries that changed. A VERSION_GUARDED_KEYS
 * entry carries `expectedVersion` (gh-#486, read from `versions`) when one is known — a stale save
 * race with another editor then 409s instead of silently overwriting; every other key is submitted
 * exactly as it always was, with no `expectedVersion` field at all.
 */
function changedEntries(
  original: Record<string, string>,
  current: Record<string, string>,
  versions: Record<string, number | undefined>
): Array<{ key: string; value: string; expectedVersion?: number }> {
  return Object.entries(current)
    .filter(([key, value]) => value !== original[key])
    .map(([key, value]) => {
      const expectedVersion = VERSION_GUARDED_KEYS.has(key) ? versions[key] : undefined;
      return expectedVersion === undefined ? { key, value } : { key, value, expectedVersion };
    });
}

/**
 * The first key in `fieldErrors` when the settings are walked in the SAME order SettingsForm's
 * own JSX renders them (`groupSettingsBySection`, section-by-section, key-by-key within a
 * section) — "DOM order" without querying the live tree. Used by the gh-#144/gh-#425 focus fix:
 * with the former per-area tab strip gone, a 400 on a field far above the Save button is
 * otherwise silent, so the first offending field takes focus instead. `null` when nothing in
 * `fieldErrors` matches a rendered key.
 */
function firstErroredKeyInDomOrder(
  settings: SettingDto[],
  fieldErrors: Record<string, string[]>
): string | null {
  for (const section of groupSettingsBySection(settings)) {
    for (const setting of section.settings) {
      if (fieldErrors[setting.key] !== undefined) return setting.key;
    }
  }
  return null;
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

export function SettingsForm({
  settings,
  libraries = [],
  timeZone,
  trailingContent,
}: SettingsFormProps): ReactNode {
  const confirm = useConfirm();
  /**
   * The last-SAVED value per key — the baseline `changedEntries` diffs against. Seeded from the
   * GET response and re-baselined after every successful PUT (gh-#140): a mount-frozen baseline
   * made any second save in the same pageview that landed back on a page-load value invisible to
   * the diff — "No changes to save." while the server still held the earlier save. Silent data
   * loss, reproduced live on the demo box.
   */
  const [original, setOriginal] = useState<Record<string, string>>(() => initialValuesFrom(settings));
  const [values, setValues] = useState<Record<string, string>>(() => initialValuesFrom(settings));
  /**
   * The last-known version token per key (gh-#486) — seeded from the GET response and re-synced
   * after every successful PUT (from that response's own fresh `SettingDto.version`, the SAME
   * shape GET returns) so the NEXT save of a VERSION_GUARDED_KEYS key carries the version THIS
   * save just produced, never a stale one. Re-baselined wholesale on a 409 refetch below.
   */
  const [versions, setVersions] = useState<Record<string, number | undefined>>(() => initialVersionsFrom(settings));
  const [status, setStatus] = useState<SaveStatus>({ kind: "idle" });
  /**
   * Per-field validation errors surfaced inline next to the relevant control. Populated when a
   * 400 is returned from PUT, keyed exactly the way the backend's own `ValidationProblemDetails`
   * keys them (gh-#425): a message under the offending setting's own key lands ONLY on that
   * field; a message under "" — ASP.NET's conventional keyless bucket, used for both an
   * empty-key entry and a cross-field `ValidateBatch` failure — has no single field to blame, so
   * it paints every CHANGED field, exactly the old aggregate behavior, but scoped to just those
   * messages (F28.9: field-level errors stay inline, never a page-wide banner).
   */
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({});
  /**
   * gh-#144/gh-#425 — the key of the first errored field (DOM order) after a 400, or `null`
   * between saves. The former per-area tab strip auto-switched to the offending tab on a 400;
   * with every section on one page now, a field far above the Save button is otherwise silent.
   * Consumed by the effect below, which focuses and scrolls to it, then clears itself back to
   * `null` — a one-shot signal, not a persisted "focused field" concept.
   */
  const [pendingFocusKey, setPendingFocusKey] = useState<string | null>(null);

  useEffect(() => {
    if (pendingFocusKey === null) return;
    // jsdom (this repo's test runner) has no scrollIntoView implementation at all — guard it,
    // never call it bare, or every spec that reaches this effect throws.
    const control = document.getElementById(`setting-${pendingFocusKey}`);
    control?.focus();
    control?.scrollIntoView?.();
    setPendingFocusKey(null);
  }, [pendingFocusKey]);

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

    const changed = changedEntries(original, values, versions);
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
      // gh-#144, T576 round 3 (R2-3) — this block never reaches the PUT, so the 400-path focus
      // effect above never fires for it; without this, nothing moves the operator to the field
      // that just silently rejected their save.
      setPendingFocusKey(blockingChange.key);
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
        // Re-baseline the diff to what was just written (gh-#140) — the saved state, not the
        // page-load state, is what the NEXT save must diff against. Merging only the submitted
        // batch keeps keys the operator kept editing during the request untouched.
        setOriginal((prev) => ({
          ...prev,
          ...Object.fromEntries(changed.map((c) => [c.key, c.value])),
        }));
        // gh-#486 — merge the just-written keys' fresh versions from the PUT response body (the
        // SAME SettingDto shape GET returns) so the NEXT save of a version-guarded key carries the
        // version THIS save just produced, not the one it started from. A malformed/non-array body
        // (an old api during a rolling deploy) clears those keys' versions instead of leaving them
        // stale — the next save of one simply skips the guard rather than false-conflicting with
        // its own prior success. Computed OUTSIDE the setVersions updater (not inside it) — a
        // throw from a `setState(updater)` callback runs during React's own render phase, past
        // this try/catch's reach entirely.
        let freshVersions: Record<string, number | undefined> | null = null;
        try {
          const updated: unknown = await resp.json();
          if (Array.isArray(updated)) freshVersions = initialVersionsFrom(updated as SettingDto[]);
        } catch {
          // malformed body — freshVersions stays null, handled below
        }
        if (freshVersions !== null) {
          const merged = freshVersions;
          setVersions((prev) => ({ ...prev, ...merged }));
        } else {
          setVersions((prev) => {
            const next = { ...prev };
            for (const { key } of changed) delete next[key];
            return next;
          });
        }
        setStatus({ kind: "idle" });
        toast.success("Settings saved.");
        return;
      }

      if (resp.status === 409) {
        // gh-#486 — the only way SettingsController.Put ever 409s: a version-guarded key
        // (Tts:Corrections, Tts:Pronunciations) moved under this write, another editor saved
        // first. "Do not silently merge" — refetch and re-baseline the WHOLE form to the server's
        // current truth (discarding every other staged-but-unsaved edit in this same batch too,
        // not only the conflicting key: this PUT is one page-wide batch, so "your view was stale"
        // applies to the batch as a whole) and tell the operator to redo their edit.
        setStatus({ kind: "idle" });
        const fresh = await fetchSettingsSnapshot();
        if (fresh !== null) {
          setOriginal(initialValuesFrom(fresh));
          setValues(initialValuesFrom(fresh));
          setVersions(initialVersionsFrom(fresh));
        } else {
          // Refetch itself failed — at minimum stop trusting the versions this PUT raced on.
          setVersions((prev) => {
            const next = { ...prev };
            for (const { key } of changed) delete next[key];
            return next;
          });
        }
        toast.error(
          "Someone else saved changes to these settings while this was saving. Reloaded the latest values — redo your edit."
        );
        return;
      }

      if (resp.status === 400) {
        // gh-#425 — the backend keys each message by the setting key it actually belongs to. A
        // message under "" (or under a key that isn't part of THIS batch at all) has no single
        // field to blame — it's batch-wide (an empty-key entry, or a cross-field ValidateBatch
        // failure) and paints every changed field, exactly the old aggregate behavior; a message
        // under a real submitted key stays scoped to that field alone, never leaking onto a
        // valid sibling in the same batch.
        const keyedErrors: Record<string, string[]> = {};
        const batchWideMessages: string[] = [];
        try {
          const raw = (await resp.json()) as unknown;
          if (isValidationProblemDetails(raw)) {
            const changedKeys = new Set(changed.map((c) => c.key));
            for (const [key, messages] of Object.entries(raw.errors)) {
              if (!Array.isArray(messages) || messages.length === 0) continue;
              if (key !== "" && changedKeys.has(key)) {
                keyedErrors[key] = messages;
              } else {
                batchWideMessages.push(...messages);
              }
            }
          }
        } catch {
          // malformed 400 body — fall through to no messages
        }

        const nextFieldErrors: Record<string, string[]> = { ...keyedErrors };
        if (batchWideMessages.length > 0) {
          for (const { key } of changed) {
            if (keyedErrors[key] === undefined) {
              nextFieldErrors[key] = batchWideMessages;
            }
          }
        }

        // Status resets to idle so isPending drops to false and the form re-enables, letting the
        // operator correct the value and retry (the K5 stuck-Saving regression class).
        setStatus({ kind: "idle" });
        setFieldErrors(nextFieldErrors);
        setPendingFocusKey(firstErroredKeyInDomOrder(settings, nextFieldErrors));
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

  return (
    <form onSubmit={(e) => { void handleSubmit(e); }} className="flex flex-col gap-6">
      {status.kind === "noChanges" && (
        <p role="status" aria-live="polite" className="text-[0.85rem] text-mute">
          No changes to save.
        </p>
      )}

      {groupSettingsBySection(settings).map((section) => (
        <SectionCard key={section.id} title={section.label}>
          {section.settings.map((setting) => (
            <SettingField
              key={setting.key}
              setting={setting}
              value={values[setting.key] ?? ""}
              savedValue={original[setting.key] ?? ""}
              errors={fieldErrors[setting.key] ?? []}
              isPending={isPending}
              libraries={libraries}
              isSafeScopeField={setting.key === SAFE_SCOPE_KEY}
              safeScopeEffectivelyEmpty={safeScopeEffectivelyEmpty}
              rotationCouplingNotice={setting.key === ARTIST_SEPARATION_KEY ? rotationCouplingNotice : null}
              timeZone={timeZone}
              onTextChange={handleTextChange(setting.key)}
              onCheckboxChange={handleCheckboxChange(setting.key)}
              onMultiSelectChange={handleMultiSelectChange(setting.key)}
              onSemanticChange={handleSemanticChange(setting.key)}
            />
          ))}
        </SectionCard>
      ))}

      {/* T145 review F3, T576 — mounted at the end of the page, after every section card: AC1
          calls for a TTS surface, and this keeps SettingsForm agnostic of which dedicated-API
          surface a page wants (the timeZone injection precedent) rather than importing
          PronunciationRulesControl directly. */}
      {trailingContent}

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
  /**
   * The last-saved value for this key (SettingsForm's re-baselined `original`) — compared
   * against `value` with the SAME string equality `changedEntries` uses, so a control's dirty
   * indicator can never disagree with what Save will actually submit.
   */
  savedValue: string;
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
  /** Test-only injection point threaded from {@link SettingsFormProps.timeZone} for the theme
   * provenance list's date formatting (SPEC F103.11, PLAN T187 review F2). */
  timeZone?: string;
  onTextChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  onCheckboxChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  onMultiSelectChange: (e: React.ChangeEvent<HTMLSelectElement>) => void;
  /** Feeds a registered control (SPEC F54.1) — see {@link SETTING_CONTROL_REGISTRY}. */
  onSemanticChange: (value: string) => void;
}

function SettingField({
  setting,
  value,
  savedValue,
  errors,
  isPending,
  libraries,
  isSafeScopeField,
  safeScopeEffectivelyEmpty,
  rotationCouplingNotice,
  timeZone,
  onTextChange,
  onCheckboxChange,
  onMultiSelectChange,
  onSemanticChange,
}: SettingFieldProps): ReactNode {
  const controlId = `setting-${setting.key}`;
  const RegisteredControl = SETTING_CONTROL_REGISTRY[setting.key];
  /**
   * gh-#145, T576 — help copy lives in the title's `?` flyover, not under the control, and is
   * carried directly on the DTO (`setting.help`, sourced server-side from `SettingCopy`/the resx)
   * rather than a client-side lookup table. The panel keeps a stable id so the field's input can
   * point `aria-describedby` at it whether or not the flyover is open (SettingHelpFlyover keeps
   * the panel mounted, merely `hidden`) — but only when there is help worth describing: a blank
   * `help` renders no flyover and no `aria-describedby` at all.
   */
  const helpId = `${controlId}-help`;
  const hasHelp = setting.help.trim() !== "";
  const describedBy = hasHelp ? helpId : undefined;

  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-1.5">
          <label htmlFor={controlId} className="text-[0.85rem] font-semibold text-ink">
            {setting.label}
            {setting.unit !== "" && (
              <span aria-label={`Unit: ${setting.unit}`} className="ml-1 font-normal text-mute">
                ({setting.unit})
              </span>
            )}
          </label>
          {hasHelp && (
            <SettingHelpFlyover settingKey={setting.key} helpId={helpId} helpText={setting.help} />
          )}
        </div>
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
          isDirty={value !== savedValue}
          choices={setting.choices}
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
            aria-describedby={describedBy}
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
          aria-describedby={describedBy}
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
          aria-describedby={describedBy}
          className="h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50"
        />
      ) : setting.kind === "choice" ? (
        // Kind-chain fallback (T175 follow-up #2): `Station:Theme` is registered above, so this
        // never fires today, but the registry entry must stay an OPTIMIZATION, not the only thing
        // standing between a Choice-kind setting and a validator-rejectable free-text/number input.
        // Without this branch a second, unregistered `kind === "choice"` setting fell all the way
        // through to the plain NUMBER input below — worse than T163's original stopgap fold into
        // the text branch, since a number input can't even hold a slug.
        <ChoiceSettingControl
          controlId={controlId}
          value={value}
          onChange={onSemanticChange}
          disabled={isPending}
          isDirty={value !== savedValue}
          choices={setting.choices}
        />
      ) : (
        <input
          id={controlId}
          name={setting.key}
          type="number"
          value={value}
          onChange={onTextChange}
          disabled={isPending}
          aria-describedby={describedBy}
          min={setting.min ?? undefined}
          max={setting.max ?? undefined}
          className="h-9 max-w-xs rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink tabular-nums disabled:opacity-50"
        />
      )}

      {setting.key === THEME_KEY && (
        <ThemeProvenanceList choices={setting.choices} timeZone={timeZone} />
      )}

      {isSafeScopeField && (
        <SafeScopeAvailabilityBadge effectivelyEmpty={safeScopeEffectivelyEmpty} />
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

/**
 * Provenance LIST for the `Station:Theme` field (SPEC F103.11, PLAN T187; review F1) — one row per
 * choice carrying provenance, not just the currently active/saved one: T186's catalog install
 * makes a theme SELECTABLE, not active, so the primary STORY-275 case (an owner just installed a
 * catalog theme and wants to confirm it landed) needs a row before that theme is ever chosen as
 * `Station:Theme`'s value. Reads straight off `choices` (SPEC F103.11/PLAN T183's already-widened
 * `Station:Theme` choice list, `StationSettingsAllowlist.ThemeChoices`) rather than a second fetch
 * or the field's `savedValue` — every choice already carries its own `importedFrom`/`importedAt`,
 * `null` for a shipped default (no `station.theme` row exists for it to read one off). Renders
 * nothing when no choice carries provenance (a shipped-only catalog, or a non-Choice-kind field
 * where `choices` is absent — never true for `Station:Theme` in practice).
 */
function ThemeProvenanceList({
  choices,
  timeZone,
}: {
  choices: readonly SettingChoice[] | undefined;
  timeZone?: string;
}): ReactNode {
  const imported = (choices ?? []).filter(
    (choice): choice is SettingChoice & { importedFrom: string; importedAt: string } =>
      choice.importedFrom != null && choice.importedAt != null
  );
  if (imported.length === 0) return null;

  return (
    <ul className="flex flex-col gap-1">
      {imported.map((choice) => (
        <li key={choice.value}>
          <ThemeProvenanceBadge
            label={choice.label}
            importedFrom={choice.importedFrom}
            importedAt={choice.importedAt}
            timeZone={timeZone}
          />
        </li>
      ))}
    </ul>
  );
}

/**
 * One imported-theme provenance row, "&lt;label&gt; — Imported · &lt;source&gt; · &lt;date&gt;" —
 * the exact three-plus-label copy SPEC F103.11 states verbatim, folded into ONE text node (not a
 * plain label beside a separately-chipped badge): React Testing Library's default text matcher
 * — and a sighted reader's own eye — never spans split text across sibling elements, so the whole
 * row rides the SAME quiet bordered chip `PersonasClient`'s own `ProvenanceBadge` (SPEC F90.7/T105)
 * uses, rather than only the "Imported · …" tail of it. Keeps that badge's UN-renamed "Imported"
 * wording deliberately: F94.4's "Hired" rename is persona-only (that spec's own text), themes were
 * never folded into that ruling. `importedFrom` renders VERBATIM — this is provenance, not
 * decoration, so it is never prettified, same rule the persona badge follows. `timeZone` is a plain
 * pass-through from {@link ThemeProvenanceList}, the house test-injection idiom.
 *
 * Kept as its own small component in this file rather than merged with `PersonasClient`'s
 * `ProvenanceBadge` into one shared component (PLAN T187 review F1's "cheap, do it" note): the
 * TEXT shapes differ (this one folds the label into the same chip; persona's leaves the name
 * outside it) — only the visual chip styling was ever duplicated, and `Chip`
 * (`components/ui/chip.tsx`, gh-#375 extraction) now owns that once, shared by both.
 */
function ThemeProvenanceBadge({
  label,
  importedFrom,
  importedAt,
  timeZone,
}: {
  label: string;
  importedFrom: string;
  importedAt: string;
  timeZone?: string;
}): ReactNode {
  return <Chip>{`${label} — Imported · ${importedFrom} · ${formatDateStamp(importedAt, { timeZone })}`}</Chip>;
}

/** 3px-radius bordered chip for the source tag, per design-aesthetic chip conventions — the shared
 * `Chip` component, with `aria-label`/`data-source` passed straight through via its own `...props`
 * spread. */
function SourceChip({ source }: { source: SettingDto["source"] }): ReactNode {
  return (
    <Chip aria-label={`Source: ${sourceLabel(source)}`} data-source={source}>
      [{sourceLabel(source)}]
    </Chip>
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
