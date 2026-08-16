"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { toast } from "@/components/ui/toast";
import { useAvatarPacks } from "@/lib/use-avatar-packs";
import { readErrorMessage } from "@/lib/problem-details";
import { clampPackDisplayText } from "../persona-catalog/avatar-format";
import { prettifySlug } from "../persona-catalog/format-slug";

export interface PersonaAvatarPackPickerProps {
  personaId: number;
  /** Matched against each pack item's own `suggestedPersona` (SPEC F128.5) — an OFFER, never
   * applied by anything this component renders on its own; only an explicit click on "Use this
   * face" ever writes. */
  personaSlug: string;
  personaName: string;
  /** Bumps `PersonasClient`'s own `avatarVersion` counter after a successful apply — see
   * `PersonaFaceEditor`'s own `onChanged` remarks for why the parent, not this component, owns
   * that state. */
  onApplied: () => void;
}

/**
 * The apply-from-pack picker (SPEC F128.5, STORY-333, PLAN T296) — every installed avatar pack's
 * own items, listed for the persona currently being edited. An item whose `suggestedPersona`
 * matches this persona's OWN `slug` renders a "Suggested" offer chip (the same wording
 * `AvatarWardrobeClient`/`AvatarItemFace` already use for the identical field elsewhere); nothing
 * here writes on its own — every row's own explicit "Use this face" click is the only write this
 * component ever issues, one `POST .../from-pack` per click, straight through
 * `PersonaAvatarController.ApplyFromPack` (T295). No bulk affordance lives here — that is the
 * roster toolbar's own `BulkApplySuggestedModal`, a deliberately separate, ONE-confirm surface
 * (SPEC F128.5's "no auto-writes" rule applies just as much to "apply every suggestion I can see
 * from this picker" as it does to any other multi-row write).
 */
export function PersonaAvatarPackPicker({
  personaId,
  personaSlug,
  personaName,
  onApplied,
}: PersonaAvatarPackPickerProps): ReactNode {
  const packsState = useAvatarPacks();
  const [applyingKey, setApplyingKey] = useState<string | null>(null);

  async function applyFromPack(packSlug: string, itemName: string): Promise<void> {
    const key = `${packSlug}::${itemName}`;
    setApplyingKey(key);
    try {
      const resp = await fetch(`/api/personas/${personaId}/avatar/from-pack`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ packSlug, itemName }),
      });
      if (resp.ok) {
        toast.success(`Face applied to "${personaName}".`);
        onApplied();
      } else {
        toast.error(await readErrorMessage(resp));
      }
    } catch {
      toast.error("Network error — check your connection");
    }
    setApplyingKey(null);
  }

  if (packsState.kind === "loading") {
    return <p className="text-[0.82rem] text-mute">Loading avatar packs…</p>;
  }

  if (packsState.kind === "error") {
    return <p className="text-[0.82rem] text-danger">Unable to load avatar packs.</p>;
  }

  if (packsState.packs.length === 0) {
    return (
      <p className="text-[0.82rem] text-mute">
        No avatar packs installed — install one from the Wardrobe&apos;s Avatars tab.
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      {packsState.packs.map((pack) => {
        const packDisplayName = clampPackDisplayText(pack.name ?? prettifySlug(pack.slug));
        return (
          <div key={pack.slug} className="rounded-[6px] border border-line bg-surface-2 p-3">
            <p className="text-[0.82rem] font-semibold text-ink">{packDisplayName}</p>
            {pack.items.length === 0 ? (
              <p className="mt-1 text-[0.78rem] text-mute">This pack declares no items.</p>
            ) : (
              <ul aria-label={`${packDisplayName} items`} className="mt-2 flex flex-wrap gap-2">
                {pack.items.map((item, index) => {
                  const isSuggested = item.suggestedPersona === personaSlug;
                  const key = `${pack.slug}::${item.name}`;
                  return (
                    <li
                      key={`${item.name}-${index}`}
                      className={`flex items-center gap-1.5 rounded-[3px] border px-2 py-1 text-[0.78rem] ${
                        isSuggested ? "border-accent bg-surface text-ink" : "border-line bg-surface text-ink"
                      }`}
                    >
                      {clampPackDisplayText(item.name)}
                      {isSuggested && <Chip>Suggested</Chip>}
                      <Button
                        type="button"
                        variant="secondary"
                        aria-label={`Use ${clampPackDisplayText(item.name)} for ${personaName}`}
                        disabled={applyingKey !== null}
                        onClick={() => {
                          void applyFromPack(pack.slug, item.name);
                        }}
                      >
                        {applyingKey === key ? "Applying…" : "Use this face"}
                      </Button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        );
      })}
    </div>
  );
}
