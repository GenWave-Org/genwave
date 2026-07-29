// Client-side wire shapes + fetcher for the Health page's container stats (gh-#148). Browser
// fetches go through the Next.js same-origin rewrite (/api/* -> api:8080), same convention as
// lib/booth-log-api.ts — never lib/api.ts's apiGet, which is server-only.

/**
 * One container row — mirrors `ContainerStatDto` (src/GenWave.Host/Api/ContainerStatDto.cs).
 * Every measurement is nullable and `null` means "unknown", never 0 — a failed stats read must
 * not render as an idle container. `state` is a plain string on the wire (docker's lifecycle
 * vocabulary: running/restarting/exited/paused/…), not a closed union: a state this UI doesn't
 * specifically style still renders as a quiet chip rather than dropping the row.
 */
export interface ContainerStat {
  /** Compose service name ("api", "engine", …) when the container carries the compose label;
   * otherwise its docker name without the leading slash. */
  name: string;
  state: string;
  /** Healthcheck verdict (healthy/unhealthy/starting); `null` when the image defines no
   * healthcheck or the inspect read degraded. */
  health: string | null;
  /** Per-core-scaled cpu percentage (the standard docker formula — see the Host's
   * `DockerCpuCalculator`). */
  cpuPercent: number | null;
  memoryUsedBytes: number | null;
  memoryLimitBytes: number | null;
  restartCount: number | null;
}

/**
 * The `GET /api/health/containers` envelope — mirrors `ContainerStatsReportDto`. Always 200:
 * when the api couldn't consult its docker-stats sidecar this is
 * `{ degraded: true, reason, containers: [] }` and the page renders "stats unavailable" from it —
 * never an error state (SPEC F28.8's quiet-degrade discipline).
 */
export interface ContainerStatsReport {
  degraded: boolean;
  reason: string | null;
  containers: ContainerStat[];
}

/** GET /api/health/containers (gh-#148) — one report per poll, no parameters. */
export async function fetchContainerStats(): Promise<ContainerStatsReport> {
  const response = await fetch("/api/health/containers", {
    credentials: "include",
    cache: "no-store",
  });
  if (!response.ok) {
    throw new Error(`GET /api/health/containers failed: ${response.status}`);
  }
  return (await response.json()) as ContainerStatsReport;
}
