"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { apiFetch } from "@/lib/api-fetch";
import {
  narrowFlaggedAttempts,
  kindLabel,
  narrowReplay,
  outcomeLabel,
  TALK_KIND_LABELS,
  todaysChecksStillDecide,
  type TalkFlaggedAttemptDto,
  type TalkKind,
  type TalkRuleDto,
  type TalkRulesMode,
} from "@/lib/talk-rules-api";

export interface TalkRulesClientProps {
  initialRules: TalkRuleDto[];
  /** `TalkRules:Mode`, or `null` when it could not be read. */
  mode: TalkRulesMode | null;
}

type FlagsState = { kind: "loading" } | { kind: "failed" } | { kind: "loaded"; lines: TalkFlaggedAttemptDto[] };

const SWITCH_ERROR = "Could not save that switch. It is back to how it was.";
const FLAGS_ERROR = "Could not load the flagged lines.";
const REPLAY_ERROR = "Could not check against last week.";
const WINDOW_DAYS = 7;
const JSON_HEADERS = { "Content-Type": "application/json" };

function switchKey(ruleId: string, kind: TalkKind): string {
  return `${ruleId}/${kind}`;
}

function totalFlagged(rule: TalkRuleDto): number {
  return rule.canCover.reduce((sum, kind) => sum + (rule.flags7d[kind] ?? 0), 0);
}

function totalBroken(rule: TalkRuleDto): number {
  return rule.canCover.reduce((sum, kind) => sum + (rule.broken7d[kind] ?? 0), 0);
}

function linesLabel(count: number): string {
  return `${count} ${count === 1 ? "line" : "lines"}`;
}

export function TalkRulesClient({ initialRules, mode }: TalkRulesClientProps): ReactNode {
  const [rules, setRules] = useState<TalkRuleDto[]>(initialRules);
  const [saving, setSaving] = useState<ReadonlySet<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [flags, setFlags] = useState<Readonly<Record<string, FlagsState>>>({});
  const [replayCounts, setReplayCounts] = useState<Readonly<Record<string, number>> | null>(null);
  const [replaying, setReplaying] = useState(false);

  function setSwitch(ruleId: string, kind: TalkKind, enabled: boolean): void {
    setRules((prev) =>
      prev.map((rule) => (rule.id === ruleId ? { ...rule, switches: { ...rule.switches, [kind]: enabled } } : rule))
    );
  }

  function setSavingFor(key: string, on: boolean): void {
    setSaving((prev) => {
      const next = new Set(prev);
      if (on) next.add(key);
      else next.delete(key);
      return next;
    });
  }

  async function toggle(rule: TalkRuleDto, kind: TalkKind): Promise<void> {
    const key = switchKey(rule.id, kind);
    const previous = rule.switches[kind] ?? false;
    setError(null);
    setSwitch(rule.id, kind, !previous);
    setSavingFor(key, true);
    try {
      await apiFetch(`/api/talk-rules/${encodeURIComponent(rule.id)}/${kind}`, {
        method: "PUT",
        headers: JSON_HEADERS,
        body: JSON.stringify({ enabled: !previous }),
      });
    } catch {
      setSwitch(rule.id, kind, previous);
      setError(SWITCH_ERROR);
    } finally {
      setSavingFor(key, false);
    }
  }

  async function toggleOpen(ruleId: string): Promise<void> {
    if (open === ruleId) {
      setOpen(null);
      return;
    }
    setOpen(ruleId);
    if (flags[ruleId]?.kind === "loaded") return;
    setFlags((prev) => ({ ...prev, [ruleId]: { kind: "loading" } }));
    try {
      const response = await apiFetch(`/api/talk-rules/${encodeURIComponent(ruleId)}/flags?days=${WINDOW_DAYS}`);
      const lines = narrowFlaggedAttempts((await response.json()) as unknown);
      setFlags((prev) => ({ ...prev, [ruleId]: { kind: "loaded", lines } }));
    } catch {
      setFlags((prev) => ({ ...prev, [ruleId]: { kind: "failed" } }));
    }
  }

  async function replay(): Promise<void> {
    setError(null);
    setReplaying(true);
    try {
      const response = await apiFetch("/api/talk-rules/replay", {
        method: "POST",
        headers: JSON_HEADERS,
        body: JSON.stringify({ days: WINDOW_DAYS }),
      });
      const report = narrowReplay((await response.json()) as unknown);
      if (report === null) {
        setError(REPLAY_ERROR);
        return;
      }
      setReplayCounts(Object.fromEntries(report.counts.map((count) => [count.ruleId, count.flagged])));
    } catch {
      setError(REPLAY_ERROR);
    } finally {
      setReplaying(false);
    }
  }

  return (
    <div className="max-w-3xl">
      {todaysChecksStillDecide(mode) && (
        <p role="note" className="rounded-[6px] border border-line bg-surface p-3 text-[0.85rem] text-ink">
          Today&apos;s checks still decide what airs. Switching a rule here does not change what airs yet.
        </p>
      )}

      <div className="mt-4 flex flex-wrap items-center gap-3">
        <Button variant="secondary" onClick={() => void replay()} disabled={replaying} aria-busy={replaying}>
          {replaying ? "Checking…" : "Check against last week"}
        </Button>
      </div>

      {error !== null && (
        <p role="alert" className="mt-3 text-[0.85rem] text-danger">
          {error}
        </p>
      )}

      <ul className="mt-4 flex flex-col gap-3">
        {rules.map((rule) => {
          const state = flags[rule.id];
          const isOpen = open === rule.id;
          const replayed = replayCounts?.[rule.id];
          const broken = totalBroken(rule);
          return (
            <li key={rule.id} className="rounded-[6px] border border-line bg-surface p-4">
              <h2 className="text-[0.95rem] font-semibold text-ink">{rule.name}</h2>
              <p className="mt-1 text-[0.85rem] text-ink">{rule.whatItDoes}</p>

              <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-2">
                {rule.canCover.map((kind) => (
                  <li key={kind}>
                    <label className="flex min-h-10 items-center gap-2 text-[0.85rem] text-ink">
                      <input
                        type="checkbox"
                        role="switch"
                        checked={rule.switches[kind] ?? false}
                        disabled={saving.has(switchKey(rule.id, kind))}
                        onChange={() => void toggle(rule, kind)}
                        aria-label={`${rule.name}: ${TALK_KIND_LABELS[kind]}`}
                      />
                      <span aria-hidden="true">{TALK_KIND_LABELS[kind]}</span>
                    </label>
                  </li>
                ))}
              </ul>

              <p className="mt-2 text-[0.8rem] text-mute">
                Flagged {linesLabel(totalFlagged(rule))} in the last 7 days
                {rule.canCover.length > 1 &&
                  ` (${rule.canCover.map((kind) => `${TALK_KIND_LABELS[kind]} ${rule.flags7d[kind] ?? 0}`).join(", ")})`}
                .
              </p>
              {broken > 0 && (
                <p className="mt-1 text-[0.8rem] text-mute">
                  Could not run {broken} {broken === 1 ? "time" : "times"} in the last 7 days.
                </p>
              )}
              {replayed !== undefined && (
                <p className="mt-1 text-[0.8rem] text-ink">
                  Checked against last week: {linesLabel(replayed)} flagged.
                </p>
              )}

              <Button
                variant="secondary"
                className="mt-3"
                aria-expanded={isOpen}
                onClick={() => void toggleOpen(rule.id)}
              >
                {isOpen ? "Hide flagged lines" : "Show flagged lines"}
              </Button>

              {isOpen && (
                <div className="mt-3 text-[0.85rem]">
                  {rule.why !== "" && <p className="text-mute">{rule.why}</p>}
                  {rule.example !== "" && <p className="mt-1 text-mute">For example: {rule.example}</p>}
                  {state?.kind === "loading" && <p className="mt-2 text-mute">Loading…</p>}
                  {state?.kind === "failed" && <p className="mt-2 text-danger">{FLAGS_ERROR}</p>}
                  {state?.kind === "loaded" && state.lines.length === 0 && (
                    <p className="mt-2 text-mute">No flagged lines in the last 7 days.</p>
                  )}
                  {state?.kind === "loaded" && state.lines.length > 0 && (
                    <ul className="mt-2 flex flex-col gap-2">
                      {state.lines.map((line) => (
                        <li key={line.checkId} className="rounded-[6px] border border-line p-3">
                          <p className="text-[0.8rem] text-mute">
                            {line.dj ?? "No DJ"} · {kindLabel(line.kind)} · {outcomeLabel(line.outcome)}
                          </p>
                          <p className="mt-1 break-words text-ink">{line.words}</p>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              )}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
