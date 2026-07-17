"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import {
  ALL_REENRICH_FIELDS,
  REENRICH_FIELD_HINTS,
  REENRICH_FIELD_LABELS,
  type ReenrichField,
} from "../reenrichFields";

/** How long the panel stays disabled after a 202 — long enough to read the
 * success toast before the operator could trigger another run on the same row. */
const COOLDOWN_MS = 1500;

interface ReanalyzePanelProps {
  mediaId: string;
  /**
   * ISO timestamp of the operator's last tag edit, or null if tags have never been
   * manually edited.  When set and the "tags" field is selected, the UI requires
   * explicit confirmation before sending (prevents silent discard of edits).
   */
  tagsEditedAt: string | null;
}

type ReenrichStatus = "idle" | "saving" | "cooldown";

export function ReanalyzePanel({ mediaId, tagsEditedAt }: ReanalyzePanelProps): ReactNode {
  const confirm = useConfirm();
  const [selectedFields, setSelectedFields] = useState<Set<ReenrichField>>(new Set());
  const [status, setStatus] = useState<ReenrichStatus>("idle");
  const cooldownTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    return () => {
      if (cooldownTimer.current !== null) clearTimeout(cooldownTimer.current);
    };
  }, []);

  const isPending = status !== "idle";

  function toggleField(field: ReenrichField): void {
    setSelectedFields((prev) => {
      const next = new Set(prev);
      if (next.has(field)) {
        next.delete(field);
      } else {
        next.add(field);
      }
      return next;
    });
  }

  async function handleSubmit(): Promise<void> {
    // If "tags" selected AND the row has prior operator edits, require confirmation.
    if (selectedFields.has("tags") && tagsEditedAt !== null) {
      const ok = await confirm({
        title: "Re-analyze tags",
        consequence: "Your prior tag edits will be discarded — re-read from file. Continue?",
        confirmLabel: "Re-analyze",
      });
      if (!ok) return;
    }

    const fields =
      selectedFields.size === 0
        ? "all"
        : ALL_REENRICH_FIELDS.filter((f) => selectedFields.has(f)).join(",");

    setStatus("saving");

    try {
      const resp = await fetch(`/api/media/${mediaId}/reenrich?fields=${fields}`, {
        method: "POST",
      });

      if (resp.status === 202) {
        toast.success("Re-analysis scheduled — will complete in a few ticks.");
        setStatus("cooldown");
        cooldownTimer.current = setTimeout(() => setStatus("idle"), COOLDOWN_MS);
        return;
      }

      if (resp.status === 400) {
        toast.error("Unknown field — check field selection and try again");
        setStatus("idle");
        return;
      }

      if (resp.status === 404) {
        toast.error("this row no longer exists");
        setStatus("idle");
        return;
      }

      if (resp.status === 403) {
        toast.error("Access denied");
        setStatus("idle");
        return;
      }

      toast.error(`Unexpected error (${resp.status})`);
      setStatus("idle");
    } catch {
      toast.error("Network error — check your connection");
      setStatus("idle");
    }
  }

  return (
    <section aria-label="Re-analyze" className="flex flex-col gap-3">
      <fieldset className="flex flex-wrap items-center gap-4" disabled={isPending}>
        <legend className="mb-1.5 w-full text-[0.82rem] font-semibold text-mute">
          Fields to re-analyze (leave all unchecked for full re-analysis)
        </legend>

        {ALL_REENRICH_FIELDS.map((field) => (
          <label
            key={field}
            title={REENRICH_FIELD_HINTS[field]}
            className="flex min-h-10 items-center gap-1.5 text-[0.85rem] text-ink"
          >
            <input
              type="checkbox"
              checked={selectedFields.has(field)}
              onChange={() => toggleField(field)}
              disabled={isPending}
            />
            {REENRICH_FIELD_LABELS[field]}
          </label>
        ))}
      </fieldset>

      <Button
        type="button"
        variant="secondary"
        onClick={() => { void handleSubmit(); }}
        disabled={isPending}
        className="self-start"
      >
        {status === "saving" ? "Scheduling…" : "Re-analyze"}
      </Button>
    </section>
  );
}
