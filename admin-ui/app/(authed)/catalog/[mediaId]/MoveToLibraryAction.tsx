"use client";

import { useEffect, useState, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import { stripWeakETag, useRowPatch } from "@/lib/use-row-patch";

interface MoveToLibraryActionProps {
  mediaId: string;
  /** The weak ETag from the loaded row — forwarded as If-Match. */
  etag: string;
  /** Currently assigned library id (null if unknown). */
  currentLibraryId: number | null;
  /** All available libraries to pick from. */
  libraries: LibraryDto[];
  /**
   * Library IDs that belong to the station scope.
   * When non-empty, picking a destination outside this set triggers a
   * pre-submit confirmation.  Pass [] when scope is not available client-side;
   * the post-response outOfScope notice still fires regardless.
   */
  scopeLibraryIds: number[];
}

type MoveStatus = "idle" | "saving";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

/**
 * Single-row move-to-library action (SPEC F28.9, STORY-090 AC2). The PATCH
 * If-Match flow and the allow-and-warn decision points (pre-submit confirm
 * only when the destination is known out-of-scope client-side; the
 * post-response `outOfScope` body field driving the "left rotation" notice
 * regardless) are unchanged from the shipped behavior — only the
 * presentation moved from `window.confirm`/inline text to useConfirm/toast.
 *
 * The PATCH runs through the shared row-PATCH hook (SPEC F31.2–F31.3,
 * STORY-104, gitea-#181): a success folds the response ETag into the locally
 * tracked version so an immediate retry after any other edit on this row
 * uses a live version; the 400 unknown-library case keeps its existing
 * message via `describeFailure`. A 409/412 conflict calls `router.refresh()`,
 * which re-fetches the page's server-rendered data and hands this component
 * a fresh `etag` prop. A prop-change effect folds that into local `version`
 * state (mirrors CatalogTable's media-reconcile effect) whenever `etag`
 * changes, so the next PATCH after a refresh — whether triggered by this
 * conflict or by any other update to the row — always carries the current
 * version. A 409 followed by an immediate in-place retry now succeeds
 * without a manual reload (F45.2, closes gitea-#202).
 */
export function MoveToLibraryAction({
  mediaId,
  etag,
  currentLibraryId,
  libraries,
  scopeLibraryIds,
}: MoveToLibraryActionProps): ReactNode {
  const router = useRouter();
  const confirm = useConfirm();
  const defaultId = currentLibraryId !== null ? currentLibraryId : (libraries[0]?.id ?? null);
  const [selectedId, setSelectedId] = useState<number | null>(defaultId);
  const [status, setStatus] = useState<MoveStatus>("idle");
  const [version, setVersion] = useState<string>(() => stripWeakETag(etag));

  // Keeps `version` in sync with the server-rendered `etag` prop whenever it changes — most
  // notably after the conflict-triggered `router.refresh()` below, which re-fetches this page's
  // server component and hands back a fresh `etag` with no matching change to this component's
  // own state (React preserves it across a refresh by default). Without this, a 409 retry would
  // keep resending the stale version and 409 again (F45.2, closes gitea-#202).
  useEffect(() => {
    setVersion(stripWeakETag(etag));
  }, [etag]);

  const { patchRow } = useRowPatch({
    onConflict: () => router.refresh(),
    describeFailure: (failure) => {
      if (failure.kind === "conflict") return "This track changed elsewhere — reload to see the latest.";
      if (failure.status === 400) return "Unknown library — check the destination and try again";
      return undefined;
    },
  });

  const isPending = status === "saving";

  async function handleMove(): Promise<void> {
    if (selectedId === null) return;

    // Pre-submit confirmation for out-of-scope destinations when scope is known.
    const isOutOfScopeDestination =
      scopeLibraryIds.length > 0 && !scopeLibraryIds.includes(selectedId);

    if (isOutOfScopeDestination) {
      const destination = libraries.find((lib) => lib.id === selectedId);
      const ok = await confirm({
        title: "Move out of rotation",
        consequence: `"${destination?.name ?? "This library"}" is not in rotation — the track will leave rotation. Continue?`,
        confirmLabel: "Move",
      });
      if (!ok) return;
    }

    setStatus("saving");

    const outcome = await patchRow({ mediaId, version }, { libraryId: selectedId });

    if (outcome.ok) {
      setVersion(outcome.version);
      const outOfScope = isRecord(outcome.body) && outcome.body.outOfScope === true;
      toast.success(
        outOfScope ? "Library updated. This track has left rotation." : "Library updated."
      );
      setStatus("idle");
      return;
    }

    // The hook already toasted the outcome (and refreshed the row on conflict).
    setStatus("idle");
  }

  return (
    <section aria-label="Move to library" className="flex flex-col gap-3">
      <div className="flex flex-col gap-1.5">
        <label htmlFor="move-library-select" className="text-[0.82rem] font-semibold text-mute">
          Destination library
        </label>
        <select
          id="move-library-select"
          value={selectedId ?? ""}
          onChange={(e) => {
            const val = parseInt(e.currentTarget.value, 10);
            setSelectedId(isNaN(val) ? null : val);
          }}
          disabled={isPending}
          className="h-9 w-64 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50"
        >
          {libraries.map((lib) => (
            <option key={lib.id} value={lib.id}>
              {lib.name}
            </option>
          ))}
        </select>
      </div>

      <Button
        type="button"
        variant="secondary"
        onClick={() => { void handleMove(); }}
        disabled={isPending || selectedId === null}
        className="self-start"
      >
        {isPending ? "Moving…" : "Move"}
      </Button>
    </section>
  );
}
