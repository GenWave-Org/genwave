"use client";

import type { ReactNode } from "react";
import Link from "next/link";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateStamp, formatUpSince } from "@/lib/format-clock";
import { cn } from "@/lib/utils";
import type { PluginStatusEntry, StatusResponse } from "@/lib/broadcast-api";
import {
  GARDENER_KIND_ORDER,
  GARDENER_OPEN_COUNT_KEY,
  gardenerCountPhrase,
  type GardenerOpenCounts,
} from "@/lib/gardener-api";

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

/**
 * SPEC F139.2, STORY-353, PLAN T334 — the sentence-ready noun phrase for one dominant-cause count,
 * singular/plural handled (mirrors `playableTracksCaption`'s own convention one tile over). Keyed
 * on the wire's own lowercase, no-separator enum spelling (`GenWave.Tts.LlmCallCause.ToString()`,
 * SPEC F73.1's existing `status`/`mode` convention) — the station's own words for each cause,
 * rather than the wire's terse identifier leaking straight onto the tile.
 *
 * No `success` key: `LlmCallCauseCounters.DominantFailure` (the api-side read this line's own
 * `dominantCause` comes from) filters `Success` out at the source — `llm.dominantCause` can never
 * carry it, so a map entry for it would be dead weight, not a missing case.
 *
 * `canceledbywindow`/`malformedresponse` ARE kept even though `DominantFailure` is called scoped to
 * `LlmCallKind.Copy` here (see `StatusController`'s own remarks) and `GenWave.Tts.LlmCopyWriter`
 * never stamps either cause — only the crosstalk lane (`CrosstalkStockWorker`/
 * `CrosstalkScriptParser`) does. Both are unreachable on THIS tile today, by construction, not by
 * omission — left in so this map stays a complete mirror of `LlmCallCause` (the same "never drop an
 * unknown kind" discipline `CAUSE_LABELS` in `LlmCallsFeed.tsx` already follows for the OTHER,
 * kind-unscoped surface) rather than something a future edit "fixes" by deleting two lines that look
 * unused.
 */
const DOMINANT_CAUSE_NOUNS: Record<string, [singular: string, plural: string]> = {
  timeout: ["timeout", "timeouts"],
  overlength: ["over-length reply", "over-length replies"],
  truthgatereject: ["truth-gate reject", "truth-gate rejects"],
  connectionfailure: ["connection failure", "connection failures"],
  canceledbywindow: ["break-window cancellation", "break-window cancellations"],
  emptycompletion: ["empty reply", "empty replies"],
  malformedresponse: ["malformed reply", "malformed replies"],
};

/**
 * SPEC F139.2, STORY-353, PLAN T334 — the red tile's "why" line, e.g. "Red: 6 timeouts in the
 * last 24h, gemma3:12b" (the F139.2 worked example's own shape, sentence-cased per house copy
 * rule). `null` whenever the api has nothing to explain (`dominantCause`/`dominantCauseCount`/
 * `dominantCauseModel` travel together — see `StatusResponse.llm`'s own remarks) — the caller only
 * invokes this once the tile is already red, so a `null` here is simply unused, never rendered as
 * an empty line. Names the true rolling window (24h, SPEC F139.2's own retention) rather than the
 * spec's illustrative "last hour" — the tile never claims a narrower window than the counters
 * actually track.
 */
function dominantCauseLine(llm: StatusResponse["llm"]): string | null {
  const cause = llm.dominantCause;
  const count = llm.dominantCauseCount;
  const model = llm.dominantCauseModel;
  if (cause == null || count == null || model == null) return null;

  const [singular, plural] = DOMINANT_CAUSE_NOUNS[cause] ?? [cause, cause];
  const noun = count === 1 ? singular : plural;
  return `Red: ${count} ${noun} in the last 24h, ${model}`;
}

/** SPEC F99.5, F100.3, STORY-256 AC4 — the Voice tile has no "disabled" state (the primary engine
 * is always configured): "warning" when the cached verdict is unhealthy, "ok" otherwise (including
 * the brief startup window before the first probe cycle completes — a degraded read is never
 * shown ahead of real evidence). */
function voiceTileVariant(voice: StatusResponse["voice"]): "ok" | "warning" {
  return voice.degraded ? "warning" : "ok";
}

/** Log-display casing only ("kokoro" -> "Kokoro") — mirrors the api's own DependencyNames casing
 * convention (FallbackTtsSynthesizer.DisplayName) so the engine name reads the same on both sides. */
function displayEngineName(engine: string): string {
  return engine.charAt(0).toUpperCase() + engine.slice(1);
}

/**
 * SPEC F149.5, STORY-368, PLAN T371 — the Rotation health tile flips to warning once MORE THAN HALF
 * the playable catalog has never aired. Reasoned, not fitted: once never-aired is the MAJORITY of
 * the pool, the envelope/persona/rotation predicates are structurally steering every pick toward the
 * same minority — the exact "9k library behaving like a 2k one" blind spot this story exists to
 * surface. A never-aired MINORITY is the ordinary shape of a healthy, growing library (recent
 * imports simply haven't had their turn yet) — 50% is the natural majority/minority split, not a
 * number tuned against a fixture. An empty playable pool has nothing to warn about (neutral).
 */
function rotationTileVariant(rotation: NonNullable<StatusResponse["rotation"]>): "neutral" | "warning" {
  if (rotation.playable === 0) return "neutral";
  return rotation.neverAired / rotation.playable > 0.5 ? "warning" : "neutral";
}

/** SPEC F149.5 — the headline's caption half: paired with `TileHeadline`'s own `value` (neverAired)
 * the same way `playableTracksCaption` pairs with the SafeScope tile's headline above. */
function rotationHeadlineCaption(playable: number): string {
  return `of ${playable} never aired`;
}

/**
 * SPEC F149.5 — the secondary line: "N aired once · M stale (90 d)". `stale` names STORY-368's own
 * `notAiredDays90` figure — a played-at-least-once track whose last airing is more than 90 days
 * back. Neither noun phrase pluralizes with the count ("1 aired once" reads fine as-is), unlike
 * `playableTracksCaption`'s "track"/"tracks" — no singular/plural branch is needed here.
 */
function rotationSecondaryLine(rotation: NonNullable<StatusResponse["rotation"]>): string {
  return `${rotation.airedOnce} aired once · ${rotation.notAiredDays90} stale (90 d)`;
}

/** SPEC F153.9, STORY-374 AC8, PLAN T378 — the Gardener tile's headline caption ("of N open"),
 * mirroring `rotationHeadlineCaption`'s own shape one tile over. */
function gardenerHeadlineCaption(total: number): string {
  return total === 1 ? "open finding" : "open findings";
}

/**
 * SPEC F153.9, STORY-374 AC8, PLAN T378 — the per-kind breakdown line, e.g. "5 dead files · 2 near
 * duplicates · 0 stale metadata · 0 unreachable · 1 shelf dust", in the SAME fixed section order
 * the Gardener page itself renders (`GARDENER_KIND_ORDER`). T371's own lesson applies here too
 * (colour is never the only signal): every count is visible text on the tile, never hover-only, so
 * a zero-open tile still names every kind at a glance rather than a bare "0".
 */
function gardenerBreakdownLine(open: GardenerOpenCounts): string {
  return GARDENER_KIND_ORDER.map((kind) => gardenerCountPhrase(kind, open[GARDENER_OPEN_COUNT_KEY[kind]])).join(" · ");
}

/** SPEC F156.7, STORY-385/386, PLAN T394 — the Plugins tile's own headline caption, singular/plural
 * handled (mirrors `playableTracksCaption`'s own convention two tiles up). Counts LOADED plugins only
 * — a skipped one is still listed below, but never inflates the headline number. */
function pluginsHeadlineCaption(loadedCount: number): string {
  return loadedCount === 1 ? "plugin loaded" : "plugins loaded";
}

/** SPEC F156.7 — one line per plugin outcome, in the api's own report order: a loaded plugin names
 * itself and its version; a skipped one adds why (T371's "colour is never the only signal" lesson,
 * one tile over — a skip reason is always visible text, never hover-only). */
function pluginLine(plugin: PluginStatusEntry): string {
  const name = plugin.name ?? "(unnamed plugin)";
  const label = plugin.version !== null ? `${name} ${plugin.version}` : name;
  return plugin.state === "loaded" ? label : `${label} — skipped${plugin.reason ? `: ${plugin.reason}` : ""}`;
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
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
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

        <Tile label="Station Imaging scope" variant={status !== null && isSafeScopeDepleted(status) ? "warning" : "neutral"}>
          {loading && <TileSkeleton />}
          {neverLoaded && <TileUnavailable />}
          {status !== null && (
            <>
              <TileHeadline value={status.safeScope.playable} caption={playableTracksCaption(status.safeScope.playable)} />
              <p className="mt-1 text-[0.8rem] text-mute">{safeScopeSubLine(status.safeScope.libraryIds)}</p>
              {isSafeScopeDepleted(status) && (
                <p className="mt-1 text-[0.75rem] font-semibold text-danger">
                  Station Imaging scope has no playable tracks — drains will be silent
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
                <>
                  <p className="mt-1 text-[0.75rem] font-semibold text-danger">
                    Last completion failed — falling back to templated copy
                  </p>
                  {/* SPEC F139.2, STORY-353, PLAN T334 — the gh-#365 acceptance ("no SSH, no
                      Loki, no darts at Llm settings"): a red tile also names WHY, not only THAT. */}
                  <DominantCauseLine llm={status.llm} />
                </>
              )}
            </>
          )}
        </Tile>

        <Tile label="Voice" variant={status !== null ? voiceTileVariant(status.voice) : "neutral"}>
          {loading && <TileSkeleton />}
          {neverLoaded && <TileUnavailable />}
          {status !== null && (
            <>
              <p className="mt-1 text-[0.9rem] text-ink">{displayEngineName(status.voice.engine)}</p>
              {status.voice.degraded ? (
                <>
                  <p className="mt-1 text-[0.75rem] font-semibold text-danger">
                    Engine down — DJ breaks are dropped, music keeps playing
                  </p>
                  {status.voice.reason !== null && (
                    <p className="mt-0.5 line-clamp-2 text-[0.72rem] text-danger">{status.voice.reason}</p>
                  )}
                </>
              ) : (
                <p className="mt-1 text-[0.8rem] text-mute">Reachable</p>
              )}
            </>
          )}
        </Tile>

        <Tile
          label="Rotation health"
          variant={status !== null && status.rotation !== undefined ? rotationTileVariant(status.rotation) : "neutral"}
        >
          {loading && <TileSkeleton />}
          {(neverLoaded || (status !== null && status.rotation === undefined)) && <TileUnavailable />}
          {status !== null && status.rotation !== undefined && (
            <>
              <TileHeadline value={status.rotation.neverAired} caption={rotationHeadlineCaption(status.rotation.playable)} />
              <p className="mt-1 text-[0.8rem] text-mute">{rotationSecondaryLine(status.rotation)}</p>
              {rotationTileVariant(status.rotation) === "warning" && (
                <p className="mt-1 text-[0.75rem] font-semibold text-danger">
                  More than half the playable catalog has never aired
                </p>
              )}
              {status.rotation.rotationSince !== null && (
                <p className="mt-0.5 text-[0.72rem] text-mute">
                  Since {formatDateStamp(status.rotation.rotationSince, { timeZone })}
                </p>
              )}
            </>
          )}
        </Tile>

        {/* SPEC F153.9, STORY-374 AC8, PLAN T378 — links to the Gardener page itself; the tile
            names both the total AND the per-kind breakdown (T371's "colour is never the only
            signal" lesson), so a warning-free neutral tile still reads as informative rather than
            "nothing to click here". */}
        <Tile label="Gardener" href="/gardener">
          {loading && <TileSkeleton />}
          {(neverLoaded || (status !== null && status.gardener === undefined)) && <TileUnavailable />}
          {status !== null && status.gardener !== undefined && (
            <>
              <TileHeadline value={status.gardener.total} caption={gardenerHeadlineCaption(status.gardener.total)} />
              <p className="mt-1 text-[0.8rem] text-mute">{gardenerBreakdownLine(status.gardener.open)}</p>
            </>
          )}
        </Tile>

        {/* SPEC F156.7, STORY-385/386 AC3, PLAN T394 — no tile at all when plugins[] is empty (a
            closed door, or a mount with zero valid plugins): non-empty implies the door is open, so
            rendering is gated on length rather than a loading/undefined check the way every OTHER
            tile above needs (this one simply has nothing to show, ever, on a closed-door install). */}
        {status !== null && status.plugins !== undefined && status.plugins.length > 0 && (
          <Tile
            label="Plugins"
            variant={status.plugins.some((plugin) => plugin.state === "skipped") ? "warning" : "ok"}
          >
            <PluginsTileBody plugins={status.plugins} />
          </Tile>
        )}
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
  /**
   * SPEC F153.9, STORY-374 AC8, PLAN T378 — when set, the whole tile becomes a `next/link` to that
   * route (the Gardener tile's own "links to /gardener" requirement) instead of a plain `role="group"`
   * div. No other tile passes this today; every tile keeps its non-interactive shape unless it opts in.
   */
  href?: string;
  children: ReactNode;
}

function Tile({ label, variant = "neutral", href, children }: TileProps): ReactNode {
  const borderClass =
    variant === "warning" ? "border-danger" : variant === "ok" ? "border-success" : "border-line";
  const baseClass = cn("rounded-[6px] border bg-surface p-4", borderClass);
  const content = (
    <>
      <p className="text-[0.68rem] font-semibold uppercase tracking-[0.14em] text-accent-2">{label}</p>
      <div className="mt-2">{children}</div>
    </>
  );

  if (href !== undefined) {
    // T378 review LOW-1: no `aria-label` override here — the link announces its OWN rendered
    // content (label + headline + breakdown line), the same way any other in-page link would,
    // rather than collapsing a screen reader's announcement down to the bare label text alone.
    return (
      <Link
        href={href}
        className={cn(
          baseClass,
          "block transition-colors duration-[120ms] ease-out hover:border-accent focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
        )}
      >
        {content}
      </Link>
    );
  }

  return (
    <div role="group" aria-label={label} className={baseClass}>
      {content}
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

/** SPEC F139.2, STORY-353, PLAN T334 — the LLM tile's "why" line, or nothing at all when the api
 * has no dominant cause to report (see `dominantCauseLine`'s own remarks). A small component
 * rather than calling `dominantCauseLine` twice at each call site (`null`-check, then render). */
function DominantCauseLine({ llm }: { llm: StatusResponse["llm"] }): ReactNode {
  const line = dominantCauseLine(llm);
  if (line === null) return null;
  return <p className="mt-0.5 text-[0.75rem] text-danger">{line}</p>;
}

/** SPEC F156.7, STORY-385/386, PLAN T394 — the Plugins tile's own body, pulled into its own
 * component (the `DominantCauseLine` precedent one function up) so `loadedCount` is computed ONCE,
 * not re-filtered for the headline value and its caption separately. */
function PluginsTileBody({ plugins }: { plugins: readonly PluginStatusEntry[] }): ReactNode {
  const loadedCount = plugins.filter((plugin) => plugin.state === "loaded").length;

  return (
    <>
      <TileHeadline value={loadedCount} caption={pluginsHeadlineCaption(loadedCount)} />
      <div className="mt-1 space-y-0.5">
        {plugins.map((plugin, index) => (
          <p key={index} className={cn("text-[0.75rem]", plugin.state === "skipped" ? "text-danger" : "text-mute")}>
            {pluginLine(plugin)}
          </p>
        ))}
      </div>
    </>
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
