"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useMemo, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { clampPackDisplayText } from "@/lib/clamp-pack-display-text";
import { readErrorMessage } from "@/lib/problem-details";
import { useAvatarPacks, type AvatarPackListEntry } from "@/lib/use-avatar-packs";
import { useRestoreFocus } from "@/lib/use-restore-focus";
import { prettifySlug } from "../persona-catalog/format-slug";
import type { PersonaDto } from "./types";

export interface BulkApplySuggestedModalProps {
  /** The current roster, matched by `slug` against every installed pack item's own
   * `suggestedPersona` (SPEC F128.5) — the SAME field `PersonaAvatarPackPicker`'s own per-row
   * highlight reads, at the roster's own scale instead of one persona's. */
  personas: PersonaDto[];
  /** Cancel, Escape, a backdrop click, or Close after the batch finishes — mirrors `FireModal`'s
   * own "the parent owns closing" shape. */
  onClose: () => void;
  /** Called once per persona id a mapping successfully applied to — the parent bumps that
   * persona's own `avatarVersion` counter (`PersonaFaceEditor`/`PersonaFace`'s own remarks) so its
   * face refreshes without a full page reload. */
  onApplied: (personaId: number) => void;
}

/** One item → persona mapping this modal offers to apply — an OFFER only (SPEC F128.5) until the
 * operator clicks Confirm. */
interface SuggestedMapping {
  key: string;
  packSlug: string;
  packDisplayName: string;
  itemName: string;
  personaId: number;
  personaName: string;
}

type RowStatus = "pending" | "applying" | "applied" | "failed";

function suggestedMappingsFrom(packs: AvatarPackListEntry[], personas: PersonaDto[]): SuggestedMapping[] {
  const personaBySlug = new Map(personas.map((p) => [p.slug, p]));
  const mappings: SuggestedMapping[] = [];
  for (const pack of packs) {
    const packDisplayName = clampPackDisplayText(pack.name ?? prettifySlug(pack.slug));
    for (const item of pack.items) {
      if (item.suggestedPersona === null) continue;
      const persona = personaBySlug.get(item.suggestedPersona);
      if (persona === undefined) continue; // SPEC F128.5: only matches where a persona with that slug actually exists
      mappings.push({
        key: `${pack.slug}::${item.name}`,
        packSlug: pack.slug,
        packDisplayName,
        itemName: item.name,
        personaId: persona.id,
        personaName: persona.name,
      });
    }
  }
  return mappings;
}

/**
 * The bulk apply-suggested confirm (SPEC F128.5, STORY-333, PLAN T296) — ONE modal listing the
 * EXACT item→persona mapping this batch is about to write, nothing applied until Confirm is
 * clicked (closing this modal at any point before that issues zero requests, by construction: no
 * `fetch` call exists anywhere in this component outside `handleConfirm`). Only matches where a
 * persona with that exact `slug` currently exists on the roster (`suggestedMappingsFrom`'s own
 * filter) — a suggestion naming a slug this station never hired is silently excluded, never shown
 * as a row this confirm could apply. Each row applies through the SAME
 * `POST .../avatar/from-pack` route the per-persona picker uses, one at a time (never parallel —
 * a batch's own failures stay easy to read in the order they were listed), and a later row's
 * failure never stops an earlier row's already-succeeded write from standing: this is "N
 * independent applies, each their own outcome", never an all-or-nothing transaction.
 */
export function BulkApplySuggestedModal({ personas, onClose, onApplied }: BulkApplySuggestedModalProps): ReactNode {
  const packsState = useAvatarPacks();
  const [rowStatus, setRowStatus] = useState<ReadonlyMap<string, RowStatus>>(new Map());
  const [rowError, setRowError] = useState<ReadonlyMap<string, string>>(new Map());
  const [phase, setPhase] = useState<"reviewing" | "applying" | "done">("reviewing");

  const mappings = useMemo(
    () => (packsState.kind === "loaded" ? suggestedMappingsFrom(packsState.packs, personas) : []),
    [packsState, personas]
  );

  // Same "capture before the dialog steals focus" idiom as `FireModal`/`confirm-dialog.tsx`
  // (gh-#465's shared hook) — this component has no `Dialog.Trigger` Radix could refocus on
  // its own.
  const restoreFocus = useRestoreFocus("on-mount");

  async function handleConfirm(): Promise<void> {
    setPhase("applying");
    let succeeded = 0;
    let failed = 0;

    for (const mapping of mappings) {
      setRowStatus((prev) => new Map(prev).set(mapping.key, "applying"));
      try {
        const resp = await fetch(`/api/personas/${mapping.personaId}/avatar/from-pack`, {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ packSlug: mapping.packSlug, itemName: mapping.itemName }),
        });
        if (resp.ok) {
          succeeded += 1;
          setRowStatus((prev) => new Map(prev).set(mapping.key, "applied"));
          onApplied(mapping.personaId);
        } else {
          failed += 1;
          const message = await readErrorMessage(resp);
          setRowStatus((prev) => new Map(prev).set(mapping.key, "failed"));
          setRowError((prev) => new Map(prev).set(mapping.key, message));
        }
      } catch {
        failed += 1;
        setRowStatus((prev) => new Map(prev).set(mapping.key, "failed"));
        setRowError((prev) => new Map(prev).set(mapping.key, "Network error — check your connection"));
      }
    }

    setPhase("done");
    if (failed === 0) {
      toast.success(`Applied ${succeeded} suggested face${succeeded === 1 ? "" : "s"}.`);
    } else {
      toast.error(`Applied ${succeeded}, failed ${failed} — see the list below.`);
    }
  }

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open && phase !== "applying") onClose();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none" />
        <Dialog.Content
          aria-label="Apply suggested faces"
          className="fixed left-1/2 top-1/2 z-50 w-[calc(100%-2rem)] max-w-xl -translate-x-1/2 -translate-y-1/2 rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={restoreFocus.onCloseAutoFocus}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">Apply suggested faces</Dialog.Title>
          <Dialog.Description className="mt-2 text-[0.85rem] text-mute">
            Every installed pack item whose suggestion matches a persona on this roster, listed
            exactly as it will be applied. Nothing is written until you confirm.
          </Dialog.Description>

          <div className="mt-4 max-h-80 overflow-y-auto rounded-[6px] border border-line">
            {packsState.kind === "loading" && <p className="p-3 text-[0.82rem] text-mute">Loading avatar packs…</p>}
            {packsState.kind === "error" && (
              <p className="p-3 text-[0.82rem] text-danger">Unable to load avatar packs.</p>
            )}
            {packsState.kind === "loaded" && mappings.length === 0 && (
              <p className="p-3 text-[0.82rem] text-mute">No suggested faces match the current roster.</p>
            )}
            {packsState.kind === "loaded" && mappings.length > 0 && (
              <ul aria-label="Suggested mapping" className="divide-y divide-line">
                {mappings.map((mapping) => {
                  const status = rowStatus.get(mapping.key) ?? "pending";
                  const error = rowError.get(mapping.key);
                  return (
                    <li key={mapping.key} className="flex flex-col gap-0.5 p-3 text-[0.82rem]">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <span className="text-ink">
                          <span className="font-semibold">{clampPackDisplayText(mapping.itemName)}</span>{" "}
                          <span className="text-mute">({mapping.packDisplayName})</span> →{" "}
                          <span className="font-semibold">{mapping.personaName}</span>
                        </span>
                        <span className="text-mute">
                          {status === "pending" && "Pending"}
                          {status === "applying" && "Applying…"}
                          {status === "applied" && "Applied"}
                          {status === "failed" && "Failed"}
                        </span>
                      </div>
                      {status === "failed" && error !== undefined && (
                        <p role="alert" className="text-danger">
                          {error}
                        </p>
                      )}
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          <div className="mt-6 flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onClose} disabled={phase === "applying"}>
              {phase === "done" ? "Close" : "Cancel"}
            </Button>
            {phase !== "done" && (
              <Button
                type="button"
                variant="primary"
                disabled={phase === "applying" || mappings.length === 0 || packsState.kind !== "loaded"}
                onClick={() => {
                  void handleConfirm();
                }}
              >
                {phase === "applying" ? "Applying…" : `Apply ${mappings.length} suggested face${mappings.length === 1 ? "" : "s"}`}
              </Button>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
