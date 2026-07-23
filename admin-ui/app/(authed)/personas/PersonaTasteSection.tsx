"use client";

import { useEffect, useState, type ReactNode } from "react";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { formatUpSince } from "@/lib/format-clock";
import { fetchPersonaTaste } from "@/lib/persona-taste-inspector-api";
import type { PersonaTasteResponse, PersonaTasteRule } from "@/lib/persona-taste-inspector-api";
import { cn } from "@/lib/utils";

interface PersonaTasteSectionProps {
  personaId: number;
  personaName: string;
}

type FetchStatus =
  | { kind: "loading" }
  | { kind: "loaded"; response: PersonaTasteResponse }
  | { kind: "error" };

// System.DayOfWeek's own wire encoding (0 = Sunday … 6 = Saturday) — see
// lib/persona-taste-inspector-api.ts's PersonaTasteRule remarks.
const DAY_LABELS = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

function formatHour(hour: number): string {
  return `${hour.toString().padStart(2, "0")}:00`;
}

/** A rule is gated when it carries a day restriction, an hour restriction, or both (SPEC F86.6's
 * "no gate" convention: empty days + both hours null means unrestricted). */
function isGated(rule: PersonaTasteRule): boolean {
  return rule.daysOfWeek.length > 0 || rule.startHour !== null || rule.endHour !== null;
}

/** Compact gate line (SPEC F86.7) — days and hour range, joined with the dashboard's own " · "
 * separator (StatusTiles' idiom) when both are present. Only ever called for a gated rule; an
 * ungated rule renders no gate line at all rather than a "none" placeholder. */
function gateSummary(rule: PersonaTasteRule): string {
  const parts: string[] = [];
  if (rule.daysOfWeek.length > 0) {
    parts.push(rule.daysOfWeek.map((day) => DAY_LABELS[day] ?? "?").join(", "));
  }
  if (rule.startHour !== null && rule.endHour !== null) {
    parts.push(`${formatHour(rule.startHour)}–${formatHour(rule.endHour)}`);
  }
  return parts.join(" · ");
}

/** Always-signed, one-decimal weight (SPEC F86.7) — collapses the float→double wire noise (e.g.
 * `0.800000011920929`) into a stable "+0.8"/"-0.6" read. One decimal place rather than PickChips'
 * own one-to-two (that component's own `formatSignedWeight`): a standing taste table reads best at
 * a single fixed width, not a diagnostics chip's occasional extra precision. */
function formatSignedWeight(weight: number): string {
  return new Intl.NumberFormat("en-US", {
    signDisplay: "always",
    minimumFractionDigits: 1,
    maximumFractionDigits: 1,
  }).format(weight);
}

/**
 * One signed weight bar spanning [-1, +1] from a center zero-axis (SPEC F86.7, design-aesthetic):
 * a dislike fills left in `--danger` (rust-red — dislikes are taste too, F82.1); a like fills
 * right in `--accent-2` (brass, the token reserved for quiet structure) — `--accent` itself is
 * never spent decorating a data bar (design-aesthetic anti-pattern).
 */
function WeightBar({ weight }: { weight: number }): ReactNode {
  const clamped = Math.max(-1, Math.min(1, weight));
  const magnitudePercent = Math.abs(clamped) * 50;
  const isNegative = clamped < 0;

  return (
    <div
      role="progressbar"
      aria-label={`Weight ${formatSignedWeight(weight)}`}
      aria-valuemin={-1}
      aria-valuemax={1}
      aria-valuenow={Math.round(clamped * 100) / 100}
      className="relative h-1.5 w-full max-w-[10rem] rounded-full bg-line"
    >
      <div aria-hidden="true" className="absolute inset-y-0 left-1/2 w-px bg-mute" />
      <div
        className={cn(
          "absolute inset-y-0 rounded-full",
          isNegative ? "right-1/2 bg-danger" : "left-1/2 bg-accent-2"
        )}
        style={{ width: `${magnitudePercent}%` }}
      />
    </div>
  );
}

/** One taste rule row: predicate summary, signed weight (number + bar), the compact gate line
 * when gated, and the row's own updated-at (SPEC F86.6-F86.7). Read-only — no control of any
 * kind renders here. */
function TasteRuleRow({ rule }: { rule: PersonaTasteRule }): ReactNode {
  const gated = isGated(rule);
  return (
    <li className="flex flex-col gap-1 border-b border-line py-2 last:border-b-0">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <span className="text-[0.85rem] font-semibold text-ink">{rule.predicateSummary}</span>
        <span className="text-[0.78rem] tabular-nums text-mute">{formatSignedWeight(rule.weight)}</span>
      </div>
      <WeightBar weight={rule.weight} />
      <div className="flex flex-wrap items-center justify-between gap-2 text-[0.72rem] text-mute">
        {gated && <span>{gateSummary(rule)}</span>}
        <span>{`Updated ${formatUpSince(rule.updatedAt)}`}</span>
      </div>
    </li>
  );
}

const GROUP_HEADING_CLASSES = "text-[0.7rem] font-semibold uppercase tracking-[0.12em] text-accent-2";

/** One source group (authored/operator/accrued) — renders nothing when it holds no rules, so an
 * empty group never clutters the section with a bare heading over nothing (SPEC F86.7). */
function TasteGroup({ label, rules }: { label: string; rules: PersonaTasteRule[] }): ReactNode {
  if (rules.length === 0) return null;
  return (
    <div>
      <h3 className={GROUP_HEADING_CLASSES}>{label}</h3>
      <ul className="mt-2 flex flex-col">
        {rules.map((rule, index) => (
          <TasteRuleRow key={`${rule.predicateSummary}-${index}`} rule={rule} />
        ))}
      </ul>
    </div>
  );
}

/** The accrued cap meter (SPEC F86.7) — count/cap read straight from the response, nothing
 * hardcoded. Rendered whenever the section has any taste at all, even when the accrued group
 * itself is currently empty (0/cap is still an honest, informative reading). Styling mirrors
 * NowPlayingCard's own dial-marking progress bar (thin bg-line track, rounded fill). */
function AccruedCapMeter({ count, cap }: { count: number; cap: number }): ReactNode {
  const percent = cap > 0 ? Math.min(100, (count / cap) * 100) : 0;
  return (
    <div>
      <div className="flex items-baseline justify-between">
        <h3 className={GROUP_HEADING_CLASSES}>Accrued</h3>
        <span className="text-[0.72rem] tabular-nums text-mute">{`${count} / ${cap}`}</span>
      </div>
      <div
        role="progressbar"
        aria-label="Accrued taste rules against the cap"
        aria-valuenow={count}
        aria-valuemin={0}
        aria-valuemax={cap}
        className="mt-1.5 h-1.5 w-full max-w-[10rem] overflow-hidden rounded-full bg-line"
      >
        <div className="h-full rounded-full bg-accent-2" style={{ width: `${percent}%` }} />
      </div>
    </div>
  );
}

/**
 * Per-row expandable, read-only Taste section (SPEC F86.7, STORY-219, PLAN T78): fetches
 * `GET /api/personas/{id}/taste` lazily on mount — this component is only ever mounted once its
 * row's disclosure is opened (`PersonasClient`), so the fetch never fires for a collapsed row.
 * Rules group under Authored/Operator/Accrued headings, each rule carries a signed weight bar
 * ([-1, +1]) plus its compact gate line when gated, and the Accrued group carries the cap meter.
 * A persona with no taste at all states that plainly rather than rendering three empty headings.
 * NO mutation control exists anywhere in this section — not even the existing taste-thumb affordance
 * (`PersonaTasteThumbs`, SPEC F84.1) reaches this inspector; it is a read surface, full stop.
 */
export function PersonaTasteSection({ personaId, personaName }: PersonaTasteSectionProps): ReactNode {
  const [status, setStatus] = useState<FetchStatus>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;

    async function load(): Promise<void> {
      try {
        const response = await fetchPersonaTaste(personaId);
        if (!cancelled) setStatus({ kind: "loaded", response });
      } catch {
        if (!cancelled) {
          setStatus({ kind: "error" });
          toast.error(`Unable to load taste for "${personaName}" — check your connection`);
        }
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [personaId, personaName]);

  return (
    <div role="region" aria-label={`Taste for ${personaName}`}>
      {status.kind === "loading" && (
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-full" />
          <Skeleton className="h-4 w-full" />
        </div>
      )}

      {status.kind === "error" && <p className="text-[0.85rem] text-mute">Taste — unavailable</p>}

      {status.kind === "loaded" &&
        (() => {
          const { response } = status;
          const hasAnyTaste =
            response.authored.length > 0 || response.operator.length > 0 || response.accrued.length > 0;

          if (!hasAnyTaste) {
            return (
              <EmptyState
                title="No taste yet"
                reason={`"${personaName}" has no authored, operator, or accrued taste opinions yet.`}
              />
            );
          }

          return (
            <div className="flex flex-col gap-4">
              <TasteGroup label="Authored" rules={response.authored} />
              <TasteGroup label="Operator" rules={response.operator} />
              <div>
                <AccruedCapMeter count={response.accruedCount} cap={response.accruedCap} />
                {response.accrued.length > 0 && (
                  <ul className="mt-2 flex flex-col">
                    {response.accrued.map((rule, index) => (
                      <TasteRuleRow key={`${rule.predicateSummary}-${index}`} rule={rule} />
                    ))}
                  </ul>
                )}
              </div>
            </div>
          );
        })()}
    </div>
  );
}
