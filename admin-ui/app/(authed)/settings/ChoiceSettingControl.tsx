"use client";

import type { ChangeEvent, ReactNode } from "react";
import type { SettingControlProps } from "./settings-types";

/** Matches SettingField's shipped single-line control styling (text/number inputs, the
 * `AudienceSettingControl` precedent). */
const CONTROL_CLASSES =
  "h-9 w-full max-w-md rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

/**
 * The generic control for any `SettingKind.Choice` setting (SPEC F102.14, STORY-265; registered
 * in `SettingsForm`'s per-key control-override registry, F54.1) — replaces T163's stopgap fold
 * of `kind === "choice"` into the plain text-input branch, which let a typo produce an
 * unresolvable value even though the backend validator would reject it. Renders `choices`
 * (`setting.choices` off the wire, `SettingDto.Choices`) as a closed `<select>`, so a value the
 * validator would reject can never be typed in the first place.
 *
 * Deliberately keyed by KIND rather than named after the one setting that uses it today
 * (`Station:Theme`): the component carries no Theme-specific knowledge, only `choices`, so
 * `SETTING_CONTROL_REGISTRY` — which registers per setting KEY, same as every other entry — can
 * point a second Choice-kind setting at this SAME component the moment one exists, with no new
 * file. A `ThemeSettingControl` would have had to be copy-pasted for that second setting; nothing
 * about the theme domain leaks into this one.
 *
 * `choices` is optional on {@link SettingControlProps} because most registered controls don't
 * carry it; for a KIND the API defines as "closed set of values", an absent or empty list is a
 * wiring bug, not a normal state — rendering an empty, unusable `<select>` would be a silent dead
 * end, so this fails visibly instead (an inline `role="alert"` in place of the control).
 *
 * Option labels are `choice.label` — the SERVER's display name (`ThemeManifest.Name` for
 * `Station:Theme`, projected through `AllowedSetting.Choices` → `SettingDto.Choices`, T175
 * closing the earlier review's ruling #3). `choice.value` (the slug) is what gets submitted; a
 * client-side prettifier (slug → title case) was deliberately never added — it would invent copy
 * that could silently disagree with the manifest's own `Name` the moment the two diverge. A label
 * is presentation only and is never itself a valid staged value.
 *
 * A staged `value` outside `choices` (e.g. a slug a since-removed theme or icon pack used to own)
 * still renders, marked "(current)" — rather than snapping the visible selection to whatever
 * option happens to sort first while the actual staged value (and thus the Save diff) is
 * untouched underneath. It has no label to show (it isn't in the API's `choices` list), so it
 * falls back to the raw value, same as before T175. STORY-337 AC6 (review finding F1) names this
 * exact shape for `Station:IconPack`'s own fail-open uninstall — "the setting shows its dangling
 * value with an inline notice, and nothing errors" — so this branch is paired with an inline
 * `<p>` notice, generic across every Choice-kind setting rather than icon-pack-specific copy:
 * kind-level shape, same as every other fact this component's own remarks already treat as
 * generic (`isDefault`, the "no choices" alert).
 *
 * A staged `value` of `""` — `Station:Theme` ships unseeded by design (T163: the precedence chain
 * already terminates at the shipped default with no config entry) — gets the SAME "don't let the
 * browser lie" treatment, via a distinct branch (T175 follow-up #1): a bare
 * `<select value="">` with no matching `<option>` silently displays whichever option the browser
 * picks first, which reads as "this theme IS selected" when nothing has actually been chosen. Two
 * consequences that made this worth its own branch rather than folding into `currentIsOffList`
 * above: an operator cannot tell "unset, using the shipped default" from "explicitly pinned", and
 * selecting the option that already *looks* selected silently stages a change — the exact AC8 trap
 * (a saved row shadowing the env/shipped default forever) triggered by a click that looks like a
 * no-op. STORY-479 (SPEC F205.7e) also disables the SYNTHESIZED placeholder, so the AC8 trap
 * can't fire by mistake — a real `""` choice a setting's own `choices` carries (`Station:IconPack`'s
 * house icons, below) is a different thing and stays pickable.
 *
 * The synthesized `""` option is gated on `!choices.some((c) => c.value === "")` (STORY-337 review
 * finding F1) — `Station:Theme` never has a real `""` row in its `choices` (T163's shipped-default
 * story above), but `Station:IconPack` DOES: `""` IS its own real house-icons choice
 * (`StationSettingsAllowlist.IconPackChoices`'s own remarks), so synthesizing a SECOND `""` option
 * on top of it stacked two `<option value="">` entries with different labels ("Station default
 * (House icons)" and the real "House icons") both submitting the same value — an unpickable
 * duplicate, not an unset one. A Choice-kind setting's own `choices` list is the single source of
 * truth for what `""` means; this branch only ever needs to invent that meaning when `choices`
 * itself is silent on it.
 *
 * The label names the ACTUAL default via `choice.isDefault` (set server-side — see
 * `SettingChoice.IsDefault`'s remarks in `StationSettingsAllowlist`) rather than either a
 * hardcoded theme name (would reintroduce Theme-specific knowledge into this generic control) or
 * always-neutral copy (would waste information the server already has: which of `choices` empty
 * actually means). Naming the choice explicitly also sidesteps a second latent bug the neutral-only
 * option would have left standing — `ThemeCatalog.All`'s embedded-resource load order does not
 * guarantee the shipped default sorts first, so as themes are added a first-option guess could
 * silently name the WRONG theme. `IsDefault` is set by an explicit slug match server-side, not by
 * list position, so this stays correct regardless of catalog order or count. Falls back to the
 * neutral "Choose…" placeholder (STORY-479, SPEC F205.7e) when no choice is flagged — `Llm:Model`
 * and `Station:Voice` today, neither of which resolves an empty value to any one default.
 *
 * `choicesStale`/`choicesFailed` (STORY-479, SPEC F205.7f/g) read straight off the same-named
 * `SettingDto` fields — see {@link SettingControlProps.choicesStale}/`.choicesFailed`.
 * `choicesFailed` forces the select `disabled` and skips the "no choices available" alert even
 * when `choices` is empty — a failed probe with nothing cached is still `choicesFailed`, not the
 * wiring-bug state that alert exists for.
 */
export function ChoiceSettingControl({
  controlId,
  value,
  onChange,
  disabled,
  choices,
  choicesStale,
  choicesFailed,
}: SettingControlProps): ReactNode {
  const failed = choicesFailed === true;
  const stale = choicesStale === true;

  if ((choices === undefined || choices.length === 0) && !failed) {
    return (
      <p role="alert" className="text-[0.85rem] text-danger">
        No choices available for this setting — the settings API returned none.
      </p>
    );
  }

  const availableChoices = choices ?? [];
  const currentIsOffList = value !== "" && !availableChoices.some((choice) => choice.value === value);
  const isUnset = value === "" && !availableChoices.some((choice) => choice.value === "");
  const defaultChoice = availableChoices.find((choice) => choice.isDefault === true);
  const unsetLabel = defaultChoice !== undefined ? `Station default (${defaultChoice.label})` : "Choose…";
  const offListNoticeId = `${controlId}-off-list-notice`;
  const staleNoticeId = `${controlId}-stale-notice`;
  const failedNoticeId = `${controlId}-failed-notice`;
  const describedByIds = [
    currentIsOffList ? offListNoticeId : null,
    stale ? staleNoticeId : null,
    failed ? failedNoticeId : null,
  ].filter((id): id is string => id !== null);

  const select = (
    <select
      id={controlId}
      value={value}
      onChange={(e: ChangeEvent<HTMLSelectElement>) => onChange(e.currentTarget.value)}
      disabled={disabled || failed}
      aria-describedby={describedByIds.length > 0 ? describedByIds.join(" ") : undefined}
      className={CONTROL_CLASSES}
    >
      {isUnset && (
        <option value="" disabled>
          {unsetLabel}
        </option>
      )}
      {currentIsOffList && <option value={value}>{`${value} (current)`}</option>}
      {availableChoices.map((choice) => (
        <option key={choice.value} value={choice.value}>
          {choice.label}
        </option>
      ))}
    </select>
  );

  if (!currentIsOffList && !stale && !failed) return select;

  return (
    <div className="flex flex-col gap-1.5">
      {select}
      {currentIsOffList && (
        // STORY-337 AC6 (review finding F1): the dangling value is already visible in the select
        // itself (the synthesized "(current)" option above) — this inline notice is the missing
        // second half of AC6's own contract, naming the state in plain language rather than
        // leaving "(current)" as the operator's only clue that something no longer resolves.
        // Generic copy, not icon-pack-specific — this component carries no per-key knowledge of
        // what an off-list value MEANS for any one setting.
        <p id={offListNoticeId} className="text-[0.78rem] text-mute">
          This value isn&apos;t one of the choices offered in this list — whatever it named may
          have been removed. Nothing is broken; pick a different option to replace it.
        </p>
      )}
      {stale && (
        <p id={staleNoticeId} className="text-[0.78rem] text-mute">
          This list may be out of date.
        </p>
      )}
      {failed && (
        <p id={failedNoticeId} className="text-[0.78rem] text-mute">
          Couldn&apos;t load the list.
        </p>
      )}
    </div>
  );
}
