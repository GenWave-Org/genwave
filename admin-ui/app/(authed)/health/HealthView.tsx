"use client";

import type { ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { usePoll } from "@/lib/use-poll";
import { cn } from "@/lib/utils";
import { fetchContainerStats, type ContainerStat } from "@/lib/container-stats-api";

/** Within the plan's 10-15s guidance (gh-#148) — same poll family as the booth log's 12s
 * (usePoll underneath, unchanged pause/resume/degrade contract): container stats are a vital-signs
 * readout, not a now-playing surface, so the slower cadence is plenty. Each poll also costs the
 * api one ~1s one-shot stats sample per running container, so hammering it buys nothing. */
const HEALTH_POLL_INTERVAL_MS = 12000;

/** Memory-bar fill switches from quiet brass to the danger token at this fraction of the limit —
 * the same "state colors are semantics, not decoration" rule as the dashboard tiles. */
const MEMORY_WARN_FRACTION = 0.9;

type ChipVariant = "ok" | "warning" | "muted";

/**
 * One chip per container, health verdict folded into lifecycle state: a running-but-unhealthy
 * container must read as *unhealthy* (danger), never a green "running"; a restarting one is a
 * crash loop in progress (danger); "starting" is a healthcheck still warming (quiet). Any state
 * this UI doesn't specifically style (paused, created, dead, a future docker addition) still
 * renders as a quiet chip with the raw word — a row never vanishes over vocabulary drift.
 */
export function stateChip(container: Pick<ContainerStat, "state" | "health">): {
  label: string;
  variant: ChipVariant;
} {
  if (container.health === "unhealthy") return { label: "unhealthy", variant: "warning" };
  if (container.state === "restarting") return { label: "restarting", variant: "warning" };
  if (container.state === "dead") return { label: "dead", variant: "warning" };
  if (container.state === "running") {
    if (container.health === "starting") return { label: "starting", variant: "muted" };
    return { label: "running", variant: "ok" };
  }
  return { label: container.state, variant: "muted" };
}

/** 1024-based units, matching what `docker stats` prints — GiB to one decimal, MiB/KiB whole. */
export function formatBytes(bytes: number): string {
  const kib = 1024;
  const mib = kib * 1024;
  const gib = mib * 1024;
  if (bytes >= gib) return `${(bytes / gib).toFixed(1)} GiB`;
  if (bytes >= mib) return `${Math.round(bytes / mib)} MiB`;
  return `${Math.round(bytes / kib)} KiB`;
}

const CHIP_CLASSES: Record<ChipVariant, string> = {
  ok: "border-success text-success",
  warning: "border-danger text-danger",
  muted: "border-line text-mute",
};

function StateChip({ container }: { container: Pick<ContainerStat, "state" | "health"> }): ReactNode {
  const chip = stateChip(container);
  return (
    <span
      className={cn(
        "inline-flex w-fit items-center rounded-[999px] border px-2 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.12em]",
        CHIP_CLASSES[chip.variant]
      )}
    >
      {chip.label}
    </span>
  );
}

function MemoryBar({ usedBytes, limitBytes }: { usedBytes: number; limitBytes: number }): ReactNode {
  const fraction = limitBytes > 0 ? Math.min(usedBytes / limitBytes, 1) : 0;
  return (
    <div>
      <div className="h-1.5 overflow-hidden rounded-[999px] bg-surface-2" aria-hidden="true">
        <div
          className={cn("h-full rounded-[999px]", fraction >= MEMORY_WARN_FRACTION ? "bg-danger" : "bg-accent-2")}
          style={{ width: `${(fraction * 100).toFixed(1)}%` }}
        />
      </div>
      <p className="mt-1 text-[0.8rem] tabular-nums text-mute">
        {formatBytes(usedBytes)} / {formatBytes(limitBytes)}
      </p>
    </div>
  );
}

function ContainerCard({ container }: { container: ContainerStat }): ReactNode {
  return (
    <div role="group" aria-label={container.name} className="rounded-[6px] border border-line bg-surface p-4">
      <div className="flex items-center justify-between gap-2">
        <p className="text-[0.95rem] font-semibold text-ink">{container.name}</p>
        <StateChip container={container} />
      </div>

      <div className="mt-3 space-y-3">
        <div className="flex items-baseline justify-between">
          <p className="text-[0.68rem] font-semibold uppercase tracking-[0.14em] text-accent-2">CPU</p>
          <p className="text-[0.9rem] font-semibold tabular-nums text-ink">
            {container.cpuPercent === null ? "—" : `${container.cpuPercent.toFixed(1)}%`}
          </p>
        </div>

        <div>
          <p className="text-[0.68rem] font-semibold uppercase tracking-[0.14em] text-accent-2">Memory</p>
          <div className="mt-1">
            {container.memoryUsedBytes !== null && container.memoryLimitBytes !== null ? (
              <MemoryBar usedBytes={container.memoryUsedBytes} limitBytes={container.memoryLimitBytes} />
            ) : (
              <p className="text-[0.8rem] text-mute">—</p>
            )}
          </div>
        </div>

        {container.restartCount !== null && container.restartCount > 0 && (
          <p className="text-[0.75rem] font-semibold text-danger">
            {container.restartCount === 1 ? "1 restart" : `${container.restartCount} restarts`}
          </p>
        )}
      </div>
    </div>
  );
}

function CardSkeleton(): ReactNode {
  return (
    <div className="rounded-[6px] border border-line bg-surface p-4">
      <div className="space-y-3">
        <Skeleton className="h-5 w-24" />
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-32" />
      </div>
    </div>
  );
}

/** The one shape both "the api says degraded" and "the poll itself failed before any data" share:
 * a calm named reason, no error styling — stats are a convenience readout, not the broadcast. */
function StatsUnavailable({ reason }: { reason: string | null }): ReactNode {
  return (
    <div className="rounded-[6px] border border-line bg-surface p-4">
      <p className="text-[0.9rem] font-semibold text-ink">Container stats unavailable</p>
      <p className="mt-1 text-[0.82rem] text-mute">
        {reason ?? "The api could not read container stats — the broadcast itself is unaffected."}
      </p>
    </div>
  );
}

/**
 * The Health page's content (gh-#148): one card per container from GET /api/health/containers,
 * polled on a 12s cadence via the shared `usePoll` hook (pause on hidden tab, quiet degrade). The
 * three non-card states: skeletons before the first poll resolves; "unavailable" when the api
 * itself can't be reached (poll error before any data); "unavailable" with the api's own reason
 * when it answers `degraded: true`. A poll failure after data has loaded keeps the stale cards
 * visible with a quiet retrying hint — never a toast, never a blank page (SPEC F28.8).
 */
export function HealthView(): ReactNode {
  const { data: report, error } = usePoll(() => fetchContainerStats(), {
    intervalMs: HEALTH_POLL_INTERVAL_MS,
  });

  const loading = report === null && !error;
  const neverLoaded = report === null && error;

  return (
    <section aria-label="Container stats">
      {loading && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <CardSkeleton />
          <CardSkeleton />
          <CardSkeleton />
        </div>
      )}

      {neverLoaded && <StatsUnavailable reason={null} />}

      {report !== null && report.degraded && <StatsUnavailable reason={report.reason} />}

      {report !== null && !report.degraded && (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {report.containers.map((container) => (
            <ContainerCard key={container.name} container={container} />
          ))}
        </div>
      )}

      {error && report !== null && (
        <p className="mt-2 text-[0.75rem] text-mute">Container stats unavailable — retrying…</p>
      )}
    </section>
  );
}
