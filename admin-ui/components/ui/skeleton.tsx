import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

interface SkeletonProps {
  className?: string;
}

/**
 * Loading placeholder for initial data fetches (SPEC F28.10) — no unstyled
 * flash, no spinner-only page. Pulses on --surface-2 to read as "still
 * loading"; `prefers-reduced-motion` collapses it to a static block
 * (SPEC F28.14, .claude/skills/design-aesthetic).
 */
export function Skeleton({ className }: SkeletonProps): ReactNode {
  return (
    <div
      role="status"
      aria-label="Loading"
      className={cn("animate-pulse rounded-[6px] bg-surface-2 motion-reduce:animate-none", className)}
    />
  );
}
