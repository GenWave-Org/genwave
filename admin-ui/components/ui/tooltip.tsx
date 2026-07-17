"use client";

import { useId, useState, type KeyboardEvent, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface TooltipProps {
  /**
   * The explanation shown on hover/keyboard focus (SPEC F62.1, F62.2) — pass the SAME string used
   * as the trigger's `aria-label` so the accessible name and the visible tooltip can never diverge.
   * Most call sites get this for free through `IconButton`, which takes one `label` prop and feeds
   * it to both this component and the wrapped `Button`'s `aria-label`.
   */
  label: string;
  /** The trigger — exactly one interactive element (a `Button`, a plain `<button>`, a Radix
   * `Dialog.Trigger`/`Dialog.Close` with `asChild`, …). */
  children: ReactNode;
  className?: string;
}

/**
 * Minimal in-house tooltip primitive (SPEC F62.2, STORY-159). `@radix-ui/react-tooltip` is not a
 * dependency of this project — only `@radix-ui/react-dialog` is (confirm-dialog.tsx, MobileNav's
 * drawer) — and adding a second Radix package for a small, self-contained hover/focus popup isn't
 * worth it when this codebase already has a house pattern for exactly this shape: an
 * absolutely-positioned panel with no popover/menu library (`ColumnsToggle`'s columns panel,
 * `BedPicker`'s results listbox). This follows the same shape, sized down to a single-line label.
 *
 * Visibility is driven by the wrapping span's own mouseenter/mouseleave/focus/blur — never a
 * hover-only CSS rule — so keyboard focus on the trigger reveals the tooltip exactly like a mouse
 * hover does (F62.2's "triggerable by keyboard focus as well as hover"). React backs
 * onFocus/onBlur with the native `focusin`/`focusout` events, which bubble, so a wrapper's handler
 * fires for a focused descendant with no extra wiring; onMouseEnter/onMouseLeave behave the same
 * way relative to the trigger's box (moving onto the trigger also enters the wrapping span).
 *
 * `aria-label` on the trigger itself (not this component) already carries the control's
 * accessible name — the popup is `role="tooltip"`, a presentational supplement for sighted
 * operators. It is deliberately NOT wired via `aria-describedby`: with identical copy in both
 * places, that would make most screen readers announce the same string twice.
 */
export function Tooltip({ label, children, className }: TooltipProps): ReactNode {
  const [visible, setVisible] = useState(false);
  const tooltipId = useId();

  function show(): void {
    setVisible(true);
  }
  function hide(): void {
    setVisible(false);
  }
  function handleKeyDown(e: KeyboardEvent<HTMLSpanElement>): void {
    if (e.key === "Escape") hide();
  }

  return (
    <span
      className={cn("relative inline-flex", className)}
      onMouseEnter={show}
      onMouseLeave={hide}
      onFocus={show}
      onBlur={hide}
      onKeyDown={handleKeyDown}
    >
      {children}
      {visible && (
        <span
          id={tooltipId}
          role="tooltip"
          className="pointer-events-none absolute left-1/2 top-full z-20 mt-1.5 -translate-x-1/2 whitespace-nowrap rounded-[6px] border border-line bg-surface px-2 py-1 text-[0.75rem] font-semibold text-ink"
        >
          {label}
        </span>
      )}
    </span>
  );
}
