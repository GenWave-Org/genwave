"use client";

import { useEffect, useState } from "react";

/**
 * Ticks once per second, re-rendering the caller with the whole-second
 * elapsed duration since `startedAt` (SPEC F28.7 — "elapsed ticks
 * client-side between polls"). Resets to the fresh duration whenever
 * `startedAt` changes (a new track started). Returns 0 while `startedAt`
 * is null (nothing airing yet).
 */
export function useElapsedSeconds(startedAt: string | null): number {
  const [elapsedSeconds, setElapsedSeconds] = useState(() => computeElapsedSeconds(startedAt));

  useEffect(() => {
    setElapsedSeconds(computeElapsedSeconds(startedAt));

    if (startedAt === null) {
      return;
    }

    const intervalId = setInterval(() => {
      setElapsedSeconds(computeElapsedSeconds(startedAt));
    }, 1000);

    return () => clearInterval(intervalId);
  }, [startedAt]);

  return elapsedSeconds;
}

function computeElapsedSeconds(startedAt: string | null): number {
  if (startedAt === null) {
    return 0;
  }
  const startedMs = new Date(startedAt).getTime();
  if (Number.isNaN(startedMs)) {
    return 0;
  }
  return Math.max(0, Math.floor((Date.now() - startedMs) / 1000));
}

/**
 * The now-playing card's clamped elapsed/total shape (SPEC F50.4). `null`
 * when the play carries no duration (F50.2's engine-initiated/`tts:*`
 * plays) — callers fall back to today's elapsed-only shape in that case.
 * `elapsedSeconds` is clamped to `[0, totalSeconds]`: crossfade/skip drift
 * can leave `startedAt` implying more elapsed time than the track actually
 * runs, and the readout/progress bar must never show negative or
 * over-100% progress.
 */
export interface TrackProgress {
  /** Elapsed seconds, clamped to the track's duration. */
  elapsedSeconds: number;
  totalSeconds: number;
  /** 0–100, clamped — the progress bar's fill percentage. */
  percent: number;
}

export function computeTrackProgress(elapsedSeconds: number, durationMs: number | null): TrackProgress | null {
  if (durationMs === null) {
    return null;
  }
  const totalSeconds = Math.max(0, Math.floor(durationMs / 1000));
  const clampedElapsedSeconds = Math.min(Math.max(elapsedSeconds, 0), totalSeconds);
  const percent = totalSeconds > 0 ? (clampedElapsedSeconds / totalSeconds) * 100 : 0;
  return { elapsedSeconds: clampedElapsedSeconds, totalSeconds, percent };
}
