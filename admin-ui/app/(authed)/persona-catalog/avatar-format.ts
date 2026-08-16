/**
 * A pack's own manifest-sourced display strings (SPEC F128.1's `packName`, an item's own `name`) are
 * unbounded free-form prose the moment they arrive off this response — mirrors `FontLibraryPackDto`'s
 * own documented "unbounded free-form prose" posture for `family`/`style` (`font-format.ts`'s own
 * sibling remarks), but with no server-side length gate of its own anywhere in `AvatarPackController`
 * (unlike an item's `name` at INSTALL time, bounded to 64 chars by `IsValidItemName` — a bound this
 * SHELF/DETAIL read, and the browsing-before-install face grid in particular, never benefits from:
 * `CatalogController.ToAvatarItemDto` projects a pre-install manifest's own item name verbatim, no
 * length gate at all).
 *
 * PLAN T294 rider 2: React already escapes every character this renders — this clamp is layout
 * protection only, never a security boundary. `MAX_DISPLAY_LENGTH` is a plain, generous ceiling for a
 * one-line card title/label, not a value SPEC F128 pins anywhere.
 */
const MAX_DISPLAY_LENGTH = 80;

/**
 * Clamps a pack's own manifest-sourced display string to `MAX_DISPLAY_LENGTH` characters, appending
 * an ellipsis when truncated — see this file's own remarks for why this exists and what it does not
 * protect against.
 */
export function clampPackDisplayText(text: string): string {
  return text.length > MAX_DISPLAY_LENGTH ? `${text.slice(0, MAX_DISPLAY_LENGTH)}…` : text;
}
