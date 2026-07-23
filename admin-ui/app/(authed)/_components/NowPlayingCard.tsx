"use client";

import type { ReactNode } from "react";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDuration } from "@/lib/format-clock";
import { computeTrackProgress, useElapsedSeconds } from "./useElapsedSeconds";
import type { NowPlayingState } from "@/lib/broadcast-api";

interface NowPlayingCardProps {
  state: NowPlayingState | null;
  error: boolean;
  /**
   * Opt-in rating-controls slot (SPEC F33.11, STORY-114) — omitted entirely by the Dashboard
   * (`RecentPlays`/this card stay read-only there, F33.12), supplied by the Live page only. A
   * plain `ReactNode` slot rather than a `votable` flag + mediaId prop: this card has no idea
   * what rating controls are or how to reach the vote/never-play endpoints, and shouldn't — the
   * caller already knows the on-air track's id and decides whether it's a catalog id worth
   * rendering controls for at all (a `tts:*` patter announcement on air gets none).
   */
  ratingControls?: ReactNode;
  /**
   * Opt-in persona-taste-thumb slot (SPEC F84.1, F84.6-F84.7) — omitted entirely by the Dashboard
   * (same read-only posture as `ratingControls`, F33.12), supplied by the Live page only, and
   * rendered on its own line below `ratingControls` so the two affordances never visually merge
   * into one control cluster (F84.7's visual-distinctness requirement extends to layout, not just
   * the controls themselves). Like `ratingControls`, this card has no idea what a taste thumb is
   * or how to resolve which booth-log row/persona it belongs to — the Live page already does that
   * resolution (see `useNowPlayingTasteAttribution`) before ever handing this a node to render.
   */
  tasteThumbControls?: ReactNode;
  /**
   * Opt-in why-this-pick slot (SPEC F86.4, STORY-218, PLAN T76) — the caller's `<PickChips />`
   * element, rendered BARE (`{pickChips}`, no wrapper `<div>`), mirroring `BoothLogFeed`'s own
   * bare `<PickChips pick={entry.pick} className="mt-1.5" />` call (PLAN T75). This is load-
   * bearing, not cosmetic: `PickChips` renders `null` not just for an absent/`undefined` pick but
   * also for a *present* pick that matched no rule and wasn't exploration
   * (`{firedRules: [], isExploration: false}` — the majority production shape). A `pickChips &&
   * <div>...</div>` wrapper here would be truthy for that element regardless of what it renders
   * internally, leaving a stray empty `<div>` in the DOM. Rendering bare instead means this card
   * never has to duplicate `PickChips`'s own render-nothing gate — the spacing/margin is the
   * caller's job via `PickChips`'s own `className` prop, exactly like `BoothLogFeed`'s call site.
   */
  pickChips?: ReactNode;
}

// Decorative dial-marking strip under the elapsed readout — a receiver
// faceplate tuning scale, not a progress fill. Rendered only when the play
// carries no duration (engine-initiated/`tts:*`, SPEC F50.2): with nothing to
// measure progress against, a percentage fill would lie about how much of
// the track is left, so this stays fixed decoration (SPEC F28.7). Plays with
// a known duration get the real proportional bar below instead (SPEC F50.4).
const DIAL_MARK_COUNT = 20;
const DIAL_MARKS = Array.from({ length: DIAL_MARK_COUNT }, (_, index) => index);

/**
 * The now-playing hero card (SPEC F28.7): the receiver faceplate. Renders
 * one of four states — loading skeleton, quiet unavailable (no data has
 * ever arrived), warming-up (503 before the feeder's first tick), drain
 * (safe rotation on air), or a live track with a client-ticking elapsed
 * readout. A stale-but-present track/drain state additionally shows a
 * quiet inline hint when the most recent poll failed (SPEC F28.8 AC5) —
 * never a toast.
 *
 * Shared, unforked, between the Dashboard (Q5) and Live (Q6) — the design
 * aesthetic treats both as the same faceplate treatment, so this card is
 * imported directly rather than duplicated or given a variant prop.
 */
export function NowPlayingCard({
  state,
  error,
  ratingControls,
  tasteThumbControls,
  pickChips,
}: NowPlayingCardProps): ReactNode {
  const startedAt = state?.kind === "track" ? state.startedAt : null;
  const elapsedSeconds = useElapsedSeconds(startedAt);
  const trackProgress =
    state?.kind === "track" ? computeTrackProgress(elapsedSeconds, state.durationMs ?? null) : null;

  return (
    <section
      aria-label="Now playing"
      className="relative rounded-[6px] border border-line bg-surface p-6"
      style={{ boxShadow: "0 0 0 4px var(--bg), 0 0 0 5px var(--line)" }}
    >
      <p className="text-[0.7rem] font-semibold uppercase tracking-[0.14em] text-accent-2">
        Now playing
      </p>

      {state === null && !error && <NowPlayingSkeleton />}

      {state === null && error && (
        <p className="mt-3 text-[0.85rem] text-mute">Now playing — unavailable</p>
      )}

      {state?.kind === "warming" && (
        <p className="mt-3 text-[0.95rem] text-ink">
          Warming up — the feeder hasn&apos;t ticked yet.
        </p>
      )}

      {state?.kind === "drain" && (
        <p className="mt-3 text-[0.95rem] text-ink">Safe rotation — drain state.</p>
      )}

      {state?.kind === "track" && (
        <div className="mt-3">
          <span className="inline-flex items-center gap-1.5 rounded-[999px] bg-accent px-2.5 py-1 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-ink">
            <span aria-hidden="true" className="onair-dot h-1.5 w-1.5 rounded-full bg-accent-ink" />
            On air
          </span>

          <h2 className="mt-3 font-display text-[1.6rem] italic text-ink">
            {state.title ?? "Unknown track"}
          </h2>
          <p className="text-[0.9rem] text-mute">{state.artist ?? "Unknown artist"}</p>
          <p className="mt-1 text-[0.82rem] tabular-nums text-mute">{state.gainDb.toFixed(2)} dB</p>

          {pickChips}

          {trackProgress === null ? (
            <>
              <div aria-hidden="true" className="mt-4 flex items-end gap-[3px]">
                {DIAL_MARKS.map((mark) => (
                  <span
                    key={mark}
                    className="w-px bg-accent-2"
                    style={{ height: mark % 4 === 0 ? "10px" : "6px" }}
                  />
                ))}
              </div>
              {/* Elapsed ticks client-side every second (SPEC F28.7) — deliberately
                  not aria-live: a screen reader announcing every second would be
                  noise, not information. */}
              <p className="mt-1 text-[0.85rem] tabular-nums text-ink">
                {formatDuration(elapsedSeconds * 1000)} elapsed
              </p>
            </>
          ) : (
            <div className="mt-4">
              {/* The real dial-marking progress bar (SPEC F50.4): a thin,
                  tokens-only fill against the track's known duration, clamped
                  so crossfade/skip drift never renders past 0% or 100%. */}
              <div
                role="progressbar"
                aria-label="Track progress"
                aria-valuenow={Math.round(trackProgress.percent)}
                aria-valuemin={0}
                aria-valuemax={100}
                className="h-1 w-full overflow-hidden rounded-full bg-line"
              >
                <div className="h-full rounded-full bg-accent" style={{ width: `${trackProgress.percent}%` }} />
              </div>
              {/* Elapsed/total ticks client-side every second (SPEC F50.4) —
                  deliberately not aria-live, matching the no-duration readout
                  above: the bar's own aria-valuenow carries the a11y state. */}
              <p className="mt-1 text-[0.85rem] tabular-nums text-ink">
                {`${formatDuration(trackProgress.elapsedSeconds * 1000)} / ${formatDuration(trackProgress.totalSeconds * 1000)}`}
              </p>
            </div>
          )}

          {ratingControls && <div className="mt-3">{ratingControls}</div>}
          {tasteThumbControls && <div className="mt-2">{tasteThumbControls}</div>}
        </div>
      )}

      {error && state !== null && (
        <p className="mt-3 text-[0.75rem] text-mute">Live updates unavailable — retrying…</p>
      )}
    </section>
  );
}

function NowPlayingSkeleton(): ReactNode {
  return (
    <div className="mt-3 space-y-2">
      <Skeleton className="h-6 w-24" />
      <Skeleton className="h-7 w-48" />
      <Skeleton className="h-4 w-32" />
    </div>
  );
}
