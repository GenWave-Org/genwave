"use client";

import { Fragment, useState, type ReactNode } from "react";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { formatUpSince } from "@/lib/format-clock";
import type { LlmCallEntry } from "@/lib/llm-calls-api";
import { cn } from "@/lib/utils";

interface LlmCallsFeedProps {
  entries: LlmCallEntry[] | null;
  error: boolean;
  /** Test-only injection point for the timestamp formatter; production omits this and gets the browser's local zone. */
  timeZone?: string;
}

const STATUS_LABELS: Record<string, string> = { ok: "Ok", failed: "Failed", timeout: "Timeout" };
const MODE_LABELS: Record<string, string> = { normal: "Normal", soft: "Soft", hard: "Hard" };

/** Border+text tone for a state chip — reuses only already-established tokens (SPEC F73.1): no
 * new solid-fill pill treatment invented for this feature, since design-aesthetic's own "3px chip"
 * idiom (already shipped one file over, BoothLogFeed's kind badge) is the established shape for a
 * state tag on this exact page. */
type ChipTone = "success" | "danger" | "brass" | "neutral";

const CHIP_TONE_CLASSES: Record<ChipTone, string> = {
  success: "border-success text-success",
  danger: "border-danger text-danger",
  brass: "border-accent-2 text-accent-2",
  neutral: "border-line text-mute",
};

function Chip({ tone, children }: { tone: ChipTone; children: ReactNode }): ReactNode {
  return (
    <span
      className={cn(
        "inline-flex w-fit items-center rounded-[3px] border px-1.5 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em]",
        CHIP_TONE_CLASSES[tone]
      )}
    >
      {children}
    </span>
  );
}

/** ok -> success, failed/timeout -> danger — both misses read the same "something's wrong" tone;
 * the label text (STATUS_LABELS) is what tells them apart. */
function statusTone(status: string): ChipTone {
  return status === "ok" ? "success" : "danger";
}

/** normal -> success (full path), soft -> brass (the same quiet "system note" treatment
 * BoothLogFeed's own mode-changed badge already uses), hard -> danger. */
function modeTone(mode: string): ChipTone {
  if (mode === "normal") return "success";
  if (mode === "soft") return "brass";
  return "danger";
}

function StatusChip({ status }: { status: string }): ReactNode {
  return <Chip tone={statusTone(status)}>{STATUS_LABELS[status] ?? status}</Chip>;
}

function ModeChip({ mode }: { mode: string }): ReactNode {
  return <Chip tone={modeTone(mode)}>{MODE_LABELS[mode] ?? mode}</Chip>;
}

/** One labeled block inside an expanded row's detail panel — "—" when the field is absent (a call
 * that faulted before prompt assembly, or a failed/timeout call with no response). */
function DetailField({ label, value }: { label: string; value: string | null }): ReactNode {
  return (
    <div>
      <p className="text-[0.68rem] font-semibold uppercase tracking-[0.1em] text-accent-2">{label}</p>
      <p className="mt-0.5 whitespace-pre-wrap text-[0.82rem] text-ink">{value ?? "—"}</p>
    </div>
  );
}

/**
 * The LLM call inspector's table (PLAN T41, STORY-196, SPEC F73.1-F73.2): newest-first rows of
 * time / status chip / mode chip / elapsed / a truncated response preview, each expandable to the
 * full system prompt, user prompt, and raw response text — admin-only debug detail, never
 * persisted (see GenWave.Tts.LlmCallRing's own remarks). Loading/empty/error idioms match the
 * booth log's own BoothLogFeed (skeleton rows, EmptyState, a quiet unavailable hint on a poll
 * failure that keeps whatever was already loaded).
 */
export function LlmCallsFeed({ entries, error, timeZone }: LlmCallsFeedProps): ReactNode {
  const [expanded, setExpanded] = useState<ReadonlySet<number>>(new Set());
  const loading = entries === null && !error;
  const neverLoaded = entries === null && error;

  function toggle(seq: number): void {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(seq)) {
        next.delete(seq);
      } else {
        next.add(seq);
      }
      return next;
    });
  }

  return (
    <section aria-label="LLM calls">
      <p className="text-[0.7rem] font-semibold uppercase tracking-[0.14em] text-accent-2">LLM calls</p>

      {loading && (
        <div className="mt-3 space-y-2">
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
          <Skeleton className="h-8 w-full" />
        </div>
      )}

      {neverLoaded && <p className="mt-3 text-[0.85rem] text-mute">LLM calls — unavailable</p>}

      {entries !== null && entries.length === 0 && (
        <div className="mt-3">
          <EmptyState
            title="No LLM calls yet"
            reason="Calls appear here as on-air renders, throttled retries, and persona previews reach the LLM."
          />
        </div>
      )}

      {entries !== null && entries.length > 0 && (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full border-collapse text-[0.85rem]">
            <thead>
              <tr className="border-b-2 border-line text-left">
                <th scope="col" className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">Time</th>
                <th scope="col" className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">Status</th>
                <th scope="col" className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">Mode</th>
                <th scope="col" className="py-2 pr-3 text-right text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">Elapsed</th>
                <th scope="col" className="py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">Response</th>
                <th scope="col" className="py-2 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">
                  <span className="sr-only">Details</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => {
                const isExpanded = expanded.has(entry.seq);
                return (
                  <Fragment key={entry.seq}>
                    <tr className="border-b border-line last:border-b-0">
                      <td className="py-2 pr-3 whitespace-nowrap tabular-nums text-mute">
                        {formatUpSince(entry.startedAt, { timeZone })}
                      </td>
                      <td className="py-2 pr-3">
                        <StatusChip status={entry.status} />
                      </td>
                      <td className="py-2 pr-3">
                        <ModeChip mode={entry.mode} />
                      </td>
                      <td className="py-2 pr-3 text-right tabular-nums text-mute">{entry.elapsedMs} ms</td>
                      <td className="max-w-xs truncate py-2 pr-3 text-ink">
                        {entry.response ?? entry.statusDetail ?? "—"}
                      </td>
                      <td className="py-2">
                        <button
                          type="button"
                          onClick={() => toggle(entry.seq)}
                          aria-expanded={isExpanded}
                          className="text-[0.78rem] font-semibold text-accent hover:underline"
                        >
                          {isExpanded ? "Hide" : "Details"}
                        </button>
                      </td>
                    </tr>
                    {isExpanded && (
                      <tr className="border-b border-line last:border-b-0">
                        <td colSpan={6} className="py-3">
                          <div className="space-y-3 rounded-[6px] border border-line bg-surface-2 p-3">
                            <DetailField label="System prompt" value={entry.promptSystem} />
                            <DetailField label="User prompt" value={entry.promptUser} />
                            <DetailField label="Response" value={entry.response} />
                            {entry.statusDetail !== null && (
                              <DetailField label="Status detail" value={entry.statusDetail} />
                            )}
                          </div>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {error && entries !== null && (
        <p className="mt-2 text-[0.75rem] text-mute">LLM calls unavailable — retrying…</p>
      )}
    </section>
  );
}
