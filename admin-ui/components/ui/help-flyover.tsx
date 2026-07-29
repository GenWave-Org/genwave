"use client";

import { useId, useState, type KeyboardEvent, type ReactNode } from "react";

interface HelpFlyoverProps {
  /** The trigger's accessible name, e.g. `Help: Accrued taste`. House idiom: always `Help: …`. */
  label: string;
  /**
   * The panel's element id. Optional — a generated id is used when omitted. Pass an explicit id
   * when something else points `aria-describedby` at the panel (the settings page's SettingField
   * does; see SettingHelpFlyover).
   */
  helpId?: string;
  /** Optional `data-testid` for the panel (the settings-help-coverage parity gate keys off it). */
  testId?: string;
  /** The help copy. Kept mounted and merely `hidden` while closed — see the remarks below. */
  children: ReactNode;
}

/**
 * The house `?` help affordance (gh-#145, generalized for gh-#209/gh-#210) — a small brass ring
 * glyph whose panel explains the thing it sits next to. Extracted verbatim from the settings
 * page's SettingHelpFlyover so every page shares ONE flyover, not per-page tooltip inventions;
 * SettingHelpFlyover now delegates here, keeping its key-derived accessible name and parity-gate
 * testid contract unchanged.
 *
 * The panel stays MOUNTED at all times and hides via the `hidden` attribute rather than
 * conditional rendering — deliberately, for two reasons:
 *   - `aria-describedby` needs a stable target: assistive tech reads a `hidden` description
 *     target, so a described field keeps its description whether or not the flyover is open.
 *   - Presence gates (the settings parity testids) keep working unchanged — the help text always
 *     exists, it just isn't always visible.
 *
 * Interaction model — hover-only tooltips are not enough here (touch has no hover, and the copy
 * is multi-sentence prose an operator may want to keep open while reading):
 *   - Hover shows transiently (mouse users skim).
 *   - Keyboard focus shows transiently (tab reveals, like the house Tooltip's F62.2 contract).
 *   - Click/Enter/tap PINS the panel open; activating again (or Escape, or blurring away)
 *     closes it. Closing via the trigger also clears the transient hover/focus state so a
 *     second tap on touch — where emulated hover never "leaves" — genuinely dismisses.
 */
export function HelpFlyover({ label, helpId, testId, children }: HelpFlyoverProps): ReactNode {
  const fallbackId = useId();
  const panelId = helpId ?? fallbackId;
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
        aria-label={label}
        aria-expanded={visible}
        aria-controls={panelId}
        onClick={handleClick}
        onKeyDown={handleKeyDown}
        onMouseEnter={() => setTransient(true)}
        onMouseLeave={() => setTransient(false)}
        onFocus={() => setTransient(true)}
        onBlur={closeAll}
        // Visually a 20px brass ring glyph; the ::after inset pseudo-element widens the hit
        // area to ~40px (design-aesthetic touch-target floor) without inflating the host row.
        className="relative inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-[999px] border border-line text-[0.68rem] font-semibold text-accent-2 transition-colors duration-[120ms] ease-out after:absolute after:-inset-2.5 after:content-[''] hover:border-accent-2 focus-visible:border-accent-2"
      >
        ?
      </button>
      <span
        id={panelId}
        role="note"
        data-testid={testId}
        hidden={!visible}
        className="absolute left-0 top-full z-20 mt-1.5 w-72 max-w-[80vw] rounded-[6px] border border-line bg-surface px-3 py-2 text-left text-[0.78rem] font-normal normal-case leading-relaxed tracking-normal text-ink"
      >
        {children}
      </span>
    </span>
  );
}
