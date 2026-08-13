"use client";

import type { CSSProperties, ReactNode } from "react";
import { Toaster as SonnerToaster, toast as sonnerToast } from "sonner";

// Wireless toast conventions (.claude/skills/design-aesthetic/SKILL.md,
// SPEC F28.9): every mutation outcome surfaces here — success and failure —
// as the shipped replacement for ad-hoc inline banners. `sonner` is themed
// entirely through the CSS custom properties it already reads (`richColors`
// switches the per-type rules on); we override those properties to Wireless
// semantic tokens instead of sonner's stock HSL palette, so the toast body
// stays on --surface with a --success/--danger accent for border/text/icon
// (currentColor), never a raw hex or Tailwind stock class. The one property
// sonner does not expose as a CSS var is its default drop shadow — Tailwind's
// `!` (important) modifier is required there because sonner injects its base
// stylesheet at runtime (after ours), so plain same-specificity classes
// aren't guaranteed to win the cascade.
const TOAST_TOKEN_VARS = {
  "--border-radius": "6px",
  "--normal-bg": "var(--surface)",
  "--normal-border": "var(--line)",
  "--normal-text": "var(--ink)",
  "--success-bg": "var(--surface)",
  "--success-border": "var(--success)",
  "--success-text": "var(--success)",
  "--error-bg": "var(--surface)",
  "--error-border": "var(--danger)",
  "--error-text": "var(--danger)",
  // Warnings ride --accent (rust — the system's attention color) rather than a new token:
  // Wireless has no dedicated warn swatch, --danger stays reserved for failures, and a
  // succeeded-with-a-caveat outcome (gh-#491's collision notice) is exactly what the primary
  // attention color is for.
  "--warning-bg": "var(--surface)",
  "--warning-border": "var(--accent)",
  "--warning-text": "var(--accent)",
} as CSSProperties;

const SUCCESS_DURATION_MS = 4000;
// Errors need more attention/read time than a success acknowledgement.
const ERROR_DURATION_MS = 8000;

/** Toast helper: mutation outcomes surface here, never as ad-hoc banners. */
export const toast = {
  success(message: string): void {
    sonnerToast.success(message, { duration: SUCCESS_DURATION_MS });
  },
  error(message: string): void {
    sonnerToast.error(message, { duration: ERROR_DURATION_MS });
  },
  // The write succeeded but carries a caveat the operator should read (first user: gh-#491's
  // rules-over-corrections collision notice) — error-tier duration for the same read-time reason.
  warning(message: string): void {
    sonnerToast.warning(message, { duration: ERROR_DURATION_MS });
  },
};

/** Mounts the toast viewport once, in the authed layout. */
export function Toaster(): ReactNode {
  return (
    <SonnerToaster
      position="bottom-right"
      richColors
      style={{ ...TOAST_TOKEN_VARS, fontFamily: "var(--font-sans)" }}
      toastOptions={{
        unstyled: false,
        classNames: {
          toast: "!shadow-none !font-sans !text-[0.85rem]",
          title: "!font-semibold",
        },
      }}
    />
  );
}
