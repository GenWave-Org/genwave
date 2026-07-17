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
