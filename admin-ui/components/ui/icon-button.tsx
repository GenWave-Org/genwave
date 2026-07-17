"use client";

import type { ButtonHTMLAttributes, ReactNode } from "react";
import type { VariantProps } from "class-variance-authority";
import { Button, buttonVariants } from "@/components/ui/button";
import { Tooltip } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

type ButtonVariant = NonNullable<VariantProps<typeof buttonVariants>["variant"]>;

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, "aria-label"> {
  /**
   * The one explanation string for this control — becomes BOTH the button's accessible name
   * (`aria-label`) and the hover/focus tooltip's copy in the same render, so the two can never
   * drift apart (SPEC F62.1's "identical copy" requirement, enforced structurally rather than by
   * convention).
   */
  label: string;
  variant?: ButtonVariant;
  children: ReactNode;
}

/**
 * Icon-only control (SPEC F62.1–F62.2, STORY-159): a 40px `Button` whose sole visible content is
 * a glyph, always paired with a Wireless-tokened tooltip that reveals on hover AND keyboard focus.
 * Every icon-only interactive control built on the shared `Button` (`RatingControls`,
 * `NeverPlayControl`, `CatalogToolbar`'s vote/never-play/restore actions) renders through this one
 * component, so the aria-label/tooltip pairing is a single, un-skippable code path rather than a
 * rule a builder could forget to apply at a new call site.
 */
export function IconButton({
  label,
  children,
  className,
  variant = "secondary",
  type = "button",
  ...props
}: IconButtonProps): ReactNode {
  return (
    <Tooltip label={label}>
      <Button type={type} variant={variant} aria-label={label} className={cn("w-10 px-0", className)} {...props}>
        {children}
      </Button>
    </Tooltip>
  );
}
