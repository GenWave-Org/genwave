"use client";

import type { ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { formatUpSince } from "@/lib/format-clock";
import { cn } from "@/lib/utils";
import type { StatusResponse } from "@/lib/broadcast-api";

/** SPEC F31.4–F31.5 — non-empty SafeScope with zero playable tracks: the drain would go silent. */
function isSafeScopeDepleted(status: StatusResponse): boolean {
  return status.safeScope.playable === 0 && status.safeScope.libraryIds.length > 0;
}

/**
 * SPEC F40.2 — the SafeScope sub-line: a labeled library count plus its ids, singular/plural
 * handled, so a bare id (e.g. "7") can never again be misread as a count (gitea-#214). F25.4's
 * empty-scope text is a separate, unchanged branch (no libraries at all is not "0 libraries").
 */
function safeScopeSubLine(libraryIds: readonly number[]): string {
  if (libraryIds.length === 0) return "No libraries in scope";
  const noun = libraryIds.length === 1 ? "library" : "libraries";
  const idNoun = libraryIds.length === 1 ? "id" : "ids";
  return `${libraryIds.length} ${noun} (${idNoun} ${libraryIds.join(", ")})`;
}

/** SPEC F40.2 — the SafeScope headline caption, singular/plural handled ("1 playable track" vs "N playable tracks"). */
function playableTracksCaption(playable: number): string {
  return playable === 1 ? "playable track" : "playable tracks";
}

/**
 * SPEC F34.8, STORY-125 — the LLM tile's three states: "neutral" while disabled (no endpoint
 * configured), "ok" while enabled with no failed attempt yet recorded, "warning" only once a real
 * on-air attempt has actually failed. A never-yet-attempted enabled writer reads as "ok" — silence
 * is not a failure.
 */
function llmTileVariant(llm: StatusResponse["llm"]): "neutral" | "ok" | "warning" {
  if (!llm.enabled) return "neutral";
  return llm.lastOutcome === "failed" ? "warning" : "ok";
}

interface StatusTilesProps {
  status: StatusResponse | null;
  error: boolean;
  /** Test-only injection point for `formatUpSince`; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

/**
 * Status tiles fed by GET /api/status (SPEC F28.6–F28.7): catalog
 * ready/enriching (+failed/unavailable as secondary), SafeScope playable
 * + library ids, and "API up since". Skeletons show while the first fetch
 * is in flight; a poll failure after data has loaded degrades to a quiet
 * inline hint under the grid, keeping the stale tiles visible.
 */
export function StatusTiles({ status, error, timeZone }: StatusTilesProps): ReactNode {
  const loading = status === null && !error;
  const neverLoaded = status === null && error;

  return (
    <section aria-label="Station status">
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Tile label="Catalog">
          {loading && <TileSkeleton />}
          {neverLoaded && <TileUnavailable />}
          {status !== null && (
            <>
              <TileHeadline value={status.catalog.ready} caption="ready" />
              <p className="mt-1 text-[0.8rem] text-mute">{status.catalog.enriching} enriching</p>
              <p className="mt-0.5 text-[0.72rem] text-mute">
                {status.catalog.failed} failed · {status.catalog.unavailable} unavailable
              </p>
            </>
          )}
        </Tile>

        <Tile label="Safe scope" variant={status !== null && isSafeScopeDepleted(status) ? "warning" : "neutral"}>
          {loading && <TileSkeleton />}
          {neverLoaded && <TileUnavailable />}
          {status !== null && (
            <>
              <TileHeadline value={status.safeScope.playable} caption={playableTracksCaption(status.safeScope.playable)} />
              <p className="mt-1 text-[0.8rem] text-mute">{safeScopeSubLine(status.safeScope.libraryIds)}</p>
              {isSafeScopeDepleted(status) && (
                <p className="mt-1 text-[0.75rem] font-semibold text-danger">
                  Safe scope has no playable tracks — drains will be silent
                </p>
              )}
            </>
          )}
        </Tile>

        <Tile label="API">
          {loading && <TileSkeleton />}
          {neverLoaded && <TileUnavailable />}
          {status !== null && (
            <p className="mt-1 text-[0.9rem] tabular-nums text-ink">
              Up since {formatUpSince(status.startedAt, { timeZone })}
            </p>
          )}
        </Tile>

        <Tile label="LLM" variant={status !== null ? llmTileVariant(status.llm) : "neutral"}>
          {loading && <TileSkeleton />}
          {neverLoaded && <TileUnavailable />}
          {status !== null && !status.llm.enabled && <p className="mt-1 text-[0.9rem] text-mute">Off</p>}
          {status !== null && status.llm.enabled && (
            <>
              <p className="mt-1 text-[0.9rem] text-ink">{status.llm.model ?? "Model not set"}</p>
              {status.llm.activePersona !== null && (
                <p className="mt-0.5 text-[0.8rem] text-mute">{status.llm.activePersona}</p>
              )}
              {status.llm.lastOutcome === "failed" && (
                <p className="mt-1 text-[0.75rem] font-semibold text-danger">
                  Last completion failed — falling back to templated copy
                </p>
              )}
            </>
          )}
        </Tile>
      </div>

      {error && status !== null && (
        <p className="mt-2 text-[0.75rem] text-mute">Status unavailable — retrying…</p>
      )}
    </section>
  );
}

interface TileProps {
  label: string;
  /**
   * "ok" swaps the tile border to the success token; "warning" swaps it to the danger token
   * (SPEC F31.5); default "neutral" is the shipped border-line treatment.
   */
  variant?: "neutral" | "ok" | "warning";
  children: ReactNode;
}

function Tile({ label, variant = "neutral", children }: TileProps): ReactNode {
  const borderClass =
    variant === "warning" ? "border-danger" : variant === "ok" ? "border-success" : "border-line";

  return (
    <div role="group" aria-label={label} className={cn("rounded-[6px] border bg-surface p-4", borderClass)}>
      <p className="text-[0.68rem] font-semibold uppercase tracking-[0.14em] text-accent-2">{label}</p>
      <div className="mt-2">{children}</div>
    </div>
  );
}

function TileHeadline({ value, caption }: { value: number; caption: string }): ReactNode {
  return (
    <p className="text-[1.4rem] font-semibold text-ink">
      <span className="tabular-nums">{value}</span>{" "}
      <span className="text-[0.75rem] font-normal text-mute">{caption}</span>
    </p>
  );
}

function TileSkeleton(): ReactNode {
  return (
    <div className="space-y-2">
      <Skeleton className="h-6 w-16" />
      <Skeleton className="h-4 w-24" />
    </div>
  );
}

function TileUnavailable(): ReactNode {
  return <p className="text-[0.82rem] text-mute">Unavailable</p>;
}
