"use client";

import type { ChangeEvent, ReactNode } from "react";
import type { SettingControlProps } from "./settings-types";

/** Parses a staged multi-choice value ("" or a JSON array string) into the checked slugs. Never
 * throws: blank/whitespace, unparseable JSON, or a non-array parse all fall back to no selection;
 * any element that isn't a string is dropped. */
function parseCheckedSlugs(value: string): string[] {
  const trimmed = value.trim();
  if (trimmed === "") return [];
  try {
    const parsed: unknown = JSON.parse(trimmed);
    if (!Array.isArray(parsed)) return [];
    return parsed.filter((slug): slug is string => typeof slug === "string");
  } catch {
    return [];
  }
}

/**
 * The generic control for any `kind === "multi-choice"` setting (SPEC F205.7g, STORY-482) — one
 * labelled checkbox per `choices` entry (in list order), submitting a JSON array of the checked
 * slugs in `choices` order, never click order.
 *
 * Emits `""`, not `"[]"`, when nothing is checked: `Crosstalk:Shows` ships unset at `""` (the same
 * shipped-default idiom `ChoiceSettingControl` documents for `Station:Theme`), so unchecking every
 * box back to that starting state reproduces the exact string the form's dirty-diff already holds
 * as `original` — a check-then-uncheck round trip stays a no-op instead of staging a phantom
 * `"" -> "[]"` change. The server-side validator already treats both as valid, so this is free.
 *
 * `disabled` (the form is saving) OR `choicesFailed` disables every checkbox. Mirrors
 * `ChoiceSettingControl`'s `choicesStale`/`choicesFailed` note copy and `aria-describedby` idiom
 * (STORY-479) for consistency. Empty `choices` with no failure is a normal state (nothing to pick
 * from yet), never a wiring-bug alert — this control is generic over `choices`, so its copy names
 * no domain of its own.
 */
export function MultiChoiceSettingControl({
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
  const availableChoices = choices ?? [];
  const checkedSlugs = new Set(parseCheckedSlugs(value));
  const groupDisabled = disabled || failed;

  const staleNoticeId = `${controlId}-stale-notice`;
  const failedNoticeId = `${controlId}-failed-notice`;
  const describedByIds = [stale ? staleNoticeId : null, failed ? failedNoticeId : null].filter(
    (id): id is string => id !== null
  );

  function handleToggle(slug: string): (e: ChangeEvent<HTMLInputElement>) => void {
    return (e) => {
      const nextChecked = new Set(checkedSlugs);
      if (e.currentTarget.checked) nextChecked.add(slug);
      else nextChecked.delete(slug);
      const nextSlugs = availableChoices
        .filter((choice) => nextChecked.has(choice.value))
        .map((choice) => choice.value);
      onChange(nextSlugs.length === 0 ? "" : JSON.stringify(nextSlugs));
    };
  }

  return (
    <div className="flex flex-col gap-1.5">
      <div
        id={controlId}
        role="group"
        aria-labelledby={`${controlId}-label`}
        aria-describedby={describedByIds.length > 0 ? describedByIds.join(" ") : undefined}
        className="flex flex-col gap-1.5"
      >
        {availableChoices.length === 0 && !failed && (
          <p className="text-[0.85rem] text-mute">Nothing to choose from yet.</p>
        )}
        {availableChoices.map((choice) => (
          <label key={choice.value} className="flex items-center gap-2 text-[0.85rem] text-ink">
            <input
              type="checkbox"
              value={choice.value}
              checked={checkedSlugs.has(choice.value)}
              onChange={handleToggle(choice.value)}
              disabled={groupDisabled}
              className="h-4 w-4 disabled:opacity-50"
            />
            {choice.label}
          </label>
        ))}
      </div>
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
