/**
 * WCAG 2.x relative-luminance / contrast-ratio helper — the ONE authored implementation.
 *
 * ⚠️ REUSE, DO NOT REIMPLEMENT (PLAN T158): this was born in `design-system-foundation.spec.ts`
 * (it is what proved `--accent-2` shipped below AA at `#8a7b3f`, and why dark deliberately
 * inverts `--accent-ink`). T158 extracted it here so `theme-shelf-contrast.spec.ts` — the
 * data-driven per-theme AA gate (SPEC F102.8, STORY-268) — imports the exact same math instead
 * of growing a second, subtly different contrast implementation. Both spec files import from
 * this module; neither defines its own `contrastRatio`.
 */

/** Parses `#rrggbb` into 0-255 channel values. */
function hexToRgb(hex: string): [number, number, number] {
  const match = /^#([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})$/.exec(hex);
  if (!match) {
    throw new Error(`not a 6-digit hex color: ${hex}`);
  }
  return [parseInt(match[1], 16), parseInt(match[2], 16), parseInt(match[3], 16)];
}

/** WCAG 2.x relative luminance of an sRGB channel value (0-255). */
function relativeLuminance([r, g, b]: [number, number, number]): number {
  const linear = (c: number): number => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b);
}

/** WCAG contrast ratio (1:1 to 21:1) between two `#rrggbb` colors. */
export function contrastRatio(hexA: string, hexB: string): number {
  const lA = relativeLuminance(hexToRgb(hexA));
  const lB = relativeLuminance(hexToRgb(hexB));
  const [lighter, darker] = lA >= lB ? [lA, lB] : [lB, lA];
  return (lighter + 0.05) / (darker + 0.05);
}

/** WCAG AA minimum contrast ratio for normal-size body text. */
export const AA_NORMAL_TEXT_MIN_CONTRAST = 4.5;
