"use client";

import { useState, type KeyboardEvent, type ReactNode } from "react";

interface SettingHelpFlyoverProps {
  /** The setting key — used for the trigger's accessible name and the panel's parity testid. */
  settingKey: string;
  /**
   * The panel's element id — SettingField builds it from `controlId` and points the field's
   * input at it via `aria-describedby`, so the help copy describes the control for assistive
   * tech whether or not the flyover is open (a `hidden` describedby target is still read).
   */
  helpId: string;
  helpText: string;
}

/**
 * The `?` help affordance at the end of every setting title (gh-#145) — replaces the always-on
 * help paragraph SettingField used to render under each control. F54 pushed help coverage to
 * 100% of the allowlist, which turned the page into a wall of prose; the copy now lives in a
 * flyover panel while inline space below the control is reserved for genuine warnings
 * (ApplyModeBadge, SafeScopeAvailabilityBadge, the rotation-coupling notice, validation errors).
 *
 * The panel stays MOUNTED at all times and hides via the `hidden` attribute rather than
 * conditional rendering — deliberately, for two reasons:
 *   - `aria-describedby` needs a stable target: assistive tech reads a `hidden` description
 *     target, so the field is described whether or not the flyover is open.
 *   - The settings-help-coverage parity gate (`setting-help-<key>` testids, one per allowlisted
 *     key) keeps working unchanged — the help text still exists per key, it just isn't always
 *     visible.
 *
 * Interaction model — hover-only tooltips are not enough here (touch has no hover, and the copy
 * is multi-sentence prose an operator may want to keep open while editing):
 *   - Hover shows transiently (mouse users skim).
 *   - Keyboard focus shows transiently (tab reveals, like the house Tooltip's F62.2 contract).
 *   - Click/Enter/tap PINS the panel open; activating again (or Escape, or blurring away)
 *     closes it. Closing via the trigger also clears the transient hover/focus state so a
 *     second tap on touch — where emulated hover never "leaves" — genuinely dismisses.
 */
/**
 * The trigger's accessible name speaks the key as words — `Help: Station Rotation RecentWindow`,
 * never `Help: Station:Rotation:RecentWindow`. Colons and underscores are technical separators
 * that are noise when announced, and keeping the literal key out of the accessible name also
 * keeps the house `getByLabelText(new RegExp(key))` spec idiom unambiguous: the field input
 * stays the only element labelled by the literal key.
 */
function spokenKeyName(settingKey: string): string {
  return settingKey.replace(/[:_]/g, " ");
}

export function SettingHelpFlyover({
  settingKey,
  helpId,
  helpText,
}: SettingHelpFlyoverProps): ReactNode {
  const [pinned, setPinned] = useState(false);
  const [transient, setTransient] = useState(false);
  const visible = pinned || transient;

  function closeAll(): void {
    setPinned(false);
    setTransient(false);
  }

  function handleClick(): void {
    if (pinned) {
      closeAll();
    } else {
      setPinned(true);
    }
  }

  function handleKeyDown(e: KeyboardEvent<HTMLButtonElement>): void {
    if (e.key === "Escape") closeAll();
  }

  return (
    <span className="relative inline-flex">
      <button
        type="button"
        aria-label={`Help: ${spokenKeyName(settingKey)}`}
        aria-expanded={visible}
        aria-controls={helpId}
        onClick={handleClick}
        onKeyDown={handleKeyDown}
        onMouseEnter={() => setTransient(true)}
        onMouseLeave={() => setTransient(false)}
        onFocus={() => setTransient(true)}
        onBlur={closeAll}
        // Visually a 20px brass ring glyph; the ::after inset pseudo-element widens the hit
        // area to ~40px (design-aesthetic touch-target floor) without inflating the title row.
        className="relative inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-[999px] border border-line text-[0.68rem] font-semibold text-accent-2 transition-colors duration-[120ms] ease-out after:absolute after:-inset-2.5 after:content-[''] hover:border-accent-2 focus-visible:border-accent-2"
      >
        ?
      </button>
      <span
        id={helpId}
        role="note"
        data-testid={`setting-help-${settingKey}`}
        hidden={!visible}
        className="absolute left-0 top-full z-20 mt-1.5 w-72 max-w-[80vw] rounded-[6px] border border-line bg-surface px-3 py-2 text-left text-[0.78rem] font-normal normal-case leading-relaxed tracking-normal text-ink"
      >
        {helpText}
      </span>
    </span>
  );
}
