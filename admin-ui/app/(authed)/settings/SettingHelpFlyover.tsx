"use client";

import type { ReactNode } from "react";
import { HelpFlyover } from "@/components/ui/help-flyover";

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
 * The mechanism itself (mounted-but-`hidden` panel, hover/focus transients, click-to-pin) now
 * lives in the shared {@link HelpFlyover} (gh-#209 extracted it so other pages reuse the exact
 * same affordance); this wrapper owns only the settings-specific contract — the key-derived
 * accessible name and the `setting-help-<key>` parity-gate testid.
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
  return (
    <HelpFlyover
      label={`Help: ${spokenKeyName(settingKey)}`}
      helpId={helpId}
      testId={`setting-help-${settingKey}`}
    >
      {helpText}
    </HelpFlyover>
  );
}
