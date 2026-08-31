"use client";

import { useCallback, useEffect, useState, type ReactNode } from "react";
import { fetchStatus, type StatusResponse } from "@/lib/broadcast-api";
import {
  fetchGardenerFindings,
  GARDENER_KIND_ORDER,
  type GardenerFindingsResponse,
  type GardenerGroupDto,
  type GardenerKind,
  type GardenerOpenCounts,
  GARDENER_OPEN_COUNT_KEY,
} from "@/lib/gardener-api";
import { GardenerSection } from "./GardenerSection";

type LoadState =
  | { kind: "loading" }
  | { kind: "loaded"; findings: GardenerFindingsResponse; open: GardenerOpenCounts | null }
  | { kind: "error" };

const EMPTY_GROUP = (kind: GardenerKind): GardenerGroupDto => ({ kind, findings: [], duplicateGroups: [] });

/**
 * The Gardener page's data owner (SPEC F153.10, STORY-374 AC9, STORY-376 AC6, PLAN T378): loads
 * `GET /api/gardener/findings?state=open&limit=1000` (ORCHESTRATOR ruling 2 — the whole open queue
 * in one page, T377's own ceiling) and `GET /api/status` (the per-kind OPEN totals, for the
 * "Showing first N of M" flat-paging caveat) in parallel, exactly ONCE on mount — no polling, this
 * is a curation console an operator opens to act on, not a live dashboard.
 *
 * Every row verb (eligibility, never-play, re-enrich, dismiss, Keep this one, Purge unavailable)
 * RE-FETCHES both endpoints afterward rather than patching local state — simple and always correct
 * against the store's own reconcile passes, which can move a row between kinds/states on their own
 * schedule independent of any one operator click; at this page's bounded size (at most 1000 rows)
 * a refetch never costs enough to earn the extra state-sync code an optimistic-update model would
 * need (ORCHESTRATOR ruling 2's own "pick re-fetch (simple, correct)" call).
 */
export function GardenerView(): ReactNode {
  const [state, setState] = useState<LoadState>({ kind: "loading" });

  const load = useCallback(async () => {
    const [findings, status] = await Promise.all([
      fetchGardenerFindings(),
      fetchStatus().catch((): StatusResponse | null => null),
    ]);
    if (findings === null) {
      setState({ kind: "error" });
      return;
    }
    setState({ kind: "loaded", findings, open: status?.gardener?.open ?? null });
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  if (state.kind === "loading") {
    return <p className="text-[0.85rem] text-mute">Loading…</p>;
  }

  if (state.kind === "error") {
    return <p className="text-[0.85rem] text-danger">Couldn&apos;t load the Gardener queue — try refreshing.</p>;
  }

  const groupsByKind = new Map<GardenerKind, GardenerGroupDto>(
    state.findings.groups.map((group) => [group.kind, group])
  );

  return (
    <div className="space-y-6">
      {GARDENER_KIND_ORDER.map((kind) => (
        <GardenerSection
          key={kind}
          kind={kind}
          group={groupsByKind.get(kind) ?? EMPTY_GROUP(kind)}
          openCount={state.open !== null ? state.open[GARDENER_OPEN_COUNT_KEY[kind]] : null}
          onChanged={() => void load()}
        />
      ))}
    </div>
  );
}
