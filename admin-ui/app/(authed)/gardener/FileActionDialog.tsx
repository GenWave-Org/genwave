"use client";

import { useState, type ReactNode } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { useRestoreFocus } from "@/lib/use-restore-focus";
import {
  confirmFileAction,
  dryRunFileAction,
  type FileActionPlanDto,
  type GardenerFileActionVerb,
} from "@/lib/gardener-api";

const VERB_LABELS: Record<GardenerFileActionVerb, string> = {
  retag: "Retag",
  rename: "Rename",
  move: "Move",
};

const VERB_OPTIONS: readonly GardenerFileActionVerb[] = ["retag", "rename", "move"];

type Phase =
  | { kind: "idle" }
  | { kind: "planning" }
  | { kind: "planned"; plan: FileActionPlanDto }
  | { kind: "confirming"; plan: FileActionPlanDto }
  | { kind: "disabled" }
  | { kind: "refused"; message: string };

export interface FileActionDialogProps {
  mediaId: number;
  onCancel: () => void;
  /** Called once a confirm actually lands `done` — the caller re-fetches (SPEC F153.10's own
   * re-fetch-never-patch posture, applied here too). */
  onChanged: () => void;
}

/**
 * The Gardener page's per-row "Fix…" dialog (SPEC F154.1-F154.5; STORY-379; PLAN T381, gh-#529):
 * verb choice (Retag/Rename/Move) with an optional target, Dry run → shows the plan (From → To as
 * monospace paths, the tag diff as a table), then Confirm (posts the plan token). A 404 from
 * dry-run (file actions disabled, SPEC F154.2) shows how to enable it; a 400/409 refusal shows the
 * rule's own message as this dialog's error line — never a path, mirroring the api's own "no path
 * in any refusal" posture.
 *
 * Bespoke Radix `Dialog` markup, not the shared `DialogShell` (mirrors `CatalogInstallConfirmModal`'s
 * own reasoning) — this content (a form, then a from/to preview and a tag-diff table) is wider than
 * a yes/no prompt.
 */
export function FileActionDialog({ mediaId, onCancel, onChanged }: FileActionDialogProps): ReactNode {
  const [verb, setVerb] = useState<GardenerFileActionVerb>("retag");
  const [target, setTarget] = useState("");
  const [phase, setPhase] = useState<Phase>({ kind: "idle" });
  const restoreFocus = useRestoreFocus("on-mount");

  const busy = phase.kind === "planning" || phase.kind === "confirming";
  const showsForm = phase.kind !== "disabled" && phase.kind !== "planned" && phase.kind !== "confirming";
  const activePlan = phase.kind === "planned" || phase.kind === "confirming" ? phase.plan : null;

  async function handleDryRun(): Promise<void> {
    setPhase({ kind: "planning" });
    const trimmedTarget = target.trim();
    const outcome = await dryRunFileAction(mediaId, verb, trimmedTarget === "" ? null : trimmedTarget);

    if (!outcome.ok) {
      setPhase(outcome.status === 404 ? { kind: "disabled" } : { kind: "refused", message: outcome.detail });
      return;
    }
    setPhase({ kind: "planned", plan: outcome.plan });
  }

  async function handleConfirm(plan: FileActionPlanDto): Promise<void> {
    setPhase({ kind: "confirming", plan });
    const outcome = await confirmFileAction(plan.planToken);

    switch (outcome.kind) {
      case "done":
        toast.success(`Done — now at ${outcome.to}`);
        onChanged();
        onCancel();
        return;
      case "conflict":
        toast.error("The row changed since the plan was made — dry run again.");
        onCancel();
        return;
      case "reverted":
        toast.error("The action didn't complete and was rolled back — nothing changed.");
        onCancel();
        return;
      case "busy":
        toast.error("The gardener is mid-scan — try again in a moment.");
        setPhase({ kind: "planned", plan });
        return;
      case "refused":
        setPhase({ kind: "refused", message: outcome.message });
        return;
      case "error":
        setPhase({ kind: "refused", message: outcome.detail });
        return;
    }
  }

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onCancel();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none" />
        <Dialog.Content
          aria-label="Fix this file"
          className="fixed left-1/2 top-1/2 z-50 flex max-h-[85vh] w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 flex-col overflow-y-auto rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={restoreFocus.onCloseAutoFocus}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">Fix this file</Dialog.Title>

          {phase.kind === "disabled" && <FileActionDisabledHint />}

          {showsForm && (
            <FileActionForm verb={verb} target={target} disabled={busy} onVerbChange={setVerb} onTargetChange={setTarget} />
          )}

          {activePlan !== null && <FileActionPlanPreview plan={activePlan} />}

          {phase.kind === "refused" && (
            <p role="alert" className="mt-3 text-[0.85rem] text-danger">
              {phase.message}
            </p>
          )}

          <div className="mt-5 flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onCancel} disabled={busy}>
              Cancel
            </Button>
            {showsForm && (
              <Button type="button" onClick={() => void handleDryRun()} disabled={busy}>
                {phase.kind === "planning" ? "Planning…" : "Dry run"}
              </Button>
            )}
            {activePlan !== null && (
              <Button type="button" onClick={() => void handleConfirm(activePlan)} disabled={phase.kind === "confirming"}>
                {phase.kind === "confirming" ? "Confirming…" : "Confirm"}
              </Button>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

function FileActionDisabledHint(): ReactNode {
  return (
    <div className="mt-3 space-y-2 text-[0.85rem] text-mute">
      <p>File actions are turned off for this station.</p>
      <p>
        Set{" "}
        <code className="rounded bg-surface-2 px-1 py-0.5 font-mono text-[0.8rem] text-ink">
          Gardener__FileActions__Enabled=true
        </code>{" "}
        and restart with the file-actions mount:
      </p>
      <pre className="overflow-x-auto rounded bg-surface-2 p-2 font-mono text-[0.75rem] text-ink">
        docker compose -f compose.yaml -f compose.fileactions.yaml up
      </pre>
    </div>
  );
}

interface FileActionFormProps {
  verb: GardenerFileActionVerb;
  target: string;
  disabled: boolean;
  onVerbChange: (verb: GardenerFileActionVerb) => void;
  onTargetChange: (target: string) => void;
}

function FileActionForm({ verb, target, disabled, onVerbChange, onTargetChange }: FileActionFormProps): ReactNode {
  return (
    <div className="mt-3 space-y-3">
      <fieldset className="space-y-1.5" disabled={disabled}>
        <legend className="text-[0.8rem] font-semibold text-ink">Action</legend>
        {VERB_OPTIONS.map((option) => (
          <label key={option} className="flex items-center gap-1.5 text-[0.85rem] text-ink">
            <input
              type="radio"
              name="file-action-verb"
              value={option}
              checked={verb === option}
              onChange={() => onVerbChange(option)}
            />
            {VERB_LABELS[option]}
          </label>
        ))}
      </fieldset>

      {verb !== "retag" && (
        <label className="block text-[0.85rem] text-ink">
          {verb === "rename" ? "New file name (optional)" : "Destination directory"}
          <input
            type="text"
            value={target}
            disabled={disabled}
            onChange={(event) => onTargetChange(event.target.value)}
            placeholder={verb === "rename" ? "Leave blank for the artist – title template" : "/media/…"}
            className="mt-1 w-full rounded-[6px] border border-line bg-surface px-3 py-2 text-[0.85rem] text-ink"
          />
        </label>
      )}
    </div>
  );
}

function FileActionPlanPreview({ plan }: { plan: FileActionPlanDto }): ReactNode {
  return (
    <div className="mt-3 space-y-3">
      <div className="space-y-1 font-mono text-[0.78rem] text-ink">
        <p className="truncate" title={plan.from}>
          From: {plan.from}
        </p>
        <p className="truncate" title={plan.to}>
          To: {plan.to}
        </p>
      </div>

      {plan.tagDiff.length > 0 && (
        <table className="w-full text-left text-[0.78rem]">
          <thead>
            <tr className="text-mute">
              <th className="pr-2 font-normal">Field</th>
              <th className="pr-2 font-normal">File</th>
              <th className="font-normal">Catalog</th>
            </tr>
          </thead>
          <tbody>
            {plan.tagDiff.map((change) => (
              <tr key={change.field} className="text-ink">
                <td className="pr-2">{change.field}</td>
                <td className="pr-2 text-mute">{change.fileValue ?? "—"}</td>
                <td>{change.catalogValue}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
