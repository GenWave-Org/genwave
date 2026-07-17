import type { ReactNode } from "react";
import Link from "next/link";
import { Button, buttonVariants } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/** Either a click handler or a link target — never both (SPEC F28.10). */
export type EmptyStateCta =
  | { label: string; onClick: () => void; href?: never }
  | { label: string; href: string; onClick?: never };

interface EmptyStateProps {
  title: string;
  /** Names the reason in plain words (calm radio-operator tone is the caller's job). */
  reason: string;
  cta?: EmptyStateCta;
  className?: string;
}

/**
 * Designed empty state for any list/table with nothing to show (SPEC
 * F28.10) — names the reason and, where an action exists, offers the CTA.
 */
export function EmptyState({ title, reason, cta, className }: EmptyStateProps): ReactNode {
  return (
    <div
      className={cn(
        "flex flex-col items-center gap-2 rounded-[6px] border border-line bg-surface px-6 py-10 text-center",
        className
      )}
    >
      <p className="font-display text-[1.1rem] text-ink">{title}</p>
      <p className="max-w-sm text-[0.85rem] text-mute">{reason}</p>
      {cta &&
        (cta.href !== undefined ? (
          <Link href={cta.href} className={cn(buttonVariants({ variant: "primary" }), "mt-2")}>
            {cta.label}
          </Link>
        ) : (
          <Button variant="primary" className="mt-2" onClick={cta.onClick}>
            {cta.label}
          </Button>
        ))}
    </div>
  );
}
