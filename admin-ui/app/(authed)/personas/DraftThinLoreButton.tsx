"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { MemoryApiError, fetchPersonaMemory, suggestLore, type PersonaMemoryRow } from "@/lib/persona-memory-api";
import type { PersonaDto } from "./types";

/** A DJ with fewer live lore notes than this is "thin" (SPEC F218.49). */
export const THIN_LORE_BELOW = 3;

interface DraftThinLoreButtonProps {
  personas: readonly PersonaDto[];
  /** Change it to make the button re-read the lore counts (a Memory section changed a DJ's notes). */
  refreshKey: number;
  /** Called once after the run with the ids of the DJs whose drafts were stored. */
  onDrafted: (personaIds: readonly number[]) => void;
}

/** Live lore = kind lore, not forgotten, not waiting for approval (approved or authored). */
function countLiveLore(rows: readonly PersonaMemoryRow[]): number {
  return rows.filter((row) => row.kind === "lore" && !row.forgotten && !row.pending).length;
}

interface LoreCounts {
  /** Live lore count per DJ whose list was read. */
  counts: ReadonlyMap<number, number>;
  /** How many DJs' lists could not be read. Unknown is never treated as thin. */
  unreadCount: number;
}

async function loadLiveLoreCounts(personas: readonly PersonaDto[]): Promise<LoreCounts> {
  const entries = await Promise.all(
    personas.map(async (p): Promise<[number, number] | null> => {
      try {
        return [p.id, countLiveLore(await fetchPersonaMemory(p.id))];
      } catch {
        return null;
      }
    })
  );
  const read = entries.filter((e): e is [number, number] => e !== null);
  return { counts: new Map(read), unreadCount: entries.length - read.length };
}

function thinDjs(personas: readonly PersonaDto[], counts: ReadonlyMap<number, number>): PersonaDto[] {
  return personas.filter((p) => (counts.get(p.id) ?? THIN_LORE_BELOW) < THIN_LORE_BELOW);
}

function djs(count: number): string {
  return count === 1 ? "1 DJ" : `${count} DJs`;
}

function sentence(message: string): string {
  return `${message.replace(/\.+$/, "")}.`;
}

/** The language model is not set up (409): every later call would fail the same way. */
const NOT_SET_UP_STATUS = 409;

interface DraftFailure {
  persona: PersonaDto;
  message: string;
}

function failureText(failures: readonly DraftFailure[], stoppedEarly: boolean): string {
  if (stoppedEarly) {
    const stopper = failures[failures.length - 1];
    const earlier = failures.slice(0, -1);
    const earlierText = earlier.length > 0 ? `${failureText(earlier, false)} ` : "";
    return `${earlierText}Drafting stopped at "${stopper?.persona.name ?? ""}": ${sentence(stopper?.message ?? "")} The other DJs were not tried.`;
  }
  const names = failures.map((f) => `"${f.persona.name}"`).join(", ");
  const messages = new Set(failures.map((f) => f.message));
  if (messages.size === 1) return `Drafting failed for ${names}: ${sentence(failures[0]?.message ?? "")}`;
  const each = failures.map((f) => `"${f.persona.name}" (${f.message.replace(/\.+$/, "")})`).join(", ");
  return `Drafting failed for ${each}.`;
}

function errorMessage(err: unknown): string {
  return err instanceof MemoryApiError ? err.message : "Something went wrong.";
}

/**
 * "Draft lore for thin DJs" (SPEC F218.49, STORY-553, PLAN T741): asks the existing suggest endpoint
 * once per DJ with fewer than 3 live lore notes, one DJ at a time. Drafts stay off air until approved.
 * The counts are read again when the button is pressed, so only DJs thin at that moment are drafted.
 * One DJ failing doesn't stop the rest, except a 409 (no language model): that stops the run.
 */
export function DraftThinLoreButton({ personas, refreshKey, onDrafted }: DraftThinLoreButtonProps): ReactNode {
  const [loaded, setLoaded] = useState<{ key: string; lore: LoreCounts } | null>(null);
  const [running, setRunning] = useState(false);
  const [runCount, setRunCount] = useState(0);
  const runningRef = useRef(false);
  const personaKey = personas.map((p) => p.id).join(",");
  // runCount makes the effect re-read the counts after a run; the effect is the only reload path.
  const loadKey = `${personaKey}#${refreshKey}#${runCount}`;

  useEffect(() => {
    let cancelled = false;
    void loadLiveLoreCounts(personas).then((lore) => {
      if (!cancelled) setLoaded({ key: loadKey, lore });
    });
    return () => {
      cancelled = true;
    };
    // personas is read through loadKey: only the set of DJs matters, not row identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadKey]);

  async function draft(): Promise<void> {
    if (runningRef.current) return;
    runningRef.current = true;
    setRunning(true);
    try {
      const before = await loadLiveLoreCounts(personas);
      if (personas.length > 0 && before.counts.size === 0) {
        toast.error("Could not read lore.");
        return;
      }
      const thin = thinDjs(personas, before.counts);
      const drafted: PersonaDto[] = [];
      const failures: DraftFailure[] = [];
      let stoppedEarly = false;
      for (const persona of thin) {
        try {
          await suggestLore(persona.id);
          drafted.push(persona);
        } catch (err) {
          failures.push({ persona, message: errorMessage(err) });
          if (err instanceof MemoryApiError && err.status === NOT_SET_UP_STATUS) {
            stoppedEarly = persona !== thin[thin.length - 1];
            break;
          }
        }
      }
      if (drafted.length > 0) onDrafted(drafted.map((p) => p.id));

      if (thin.length === 0) {
        toast.success("No DJ has fewer than 3 lore notes right now.");
      } else if (failures.length === 0) {
        toast.success(`Drafted lore for ${djs(drafted.length)}. Approve the ones you like on each Lore tab.`);
      } else {
        const lead = drafted.length === 0 ? "No lore drafted." : `Drafted lore for ${djs(drafted.length)}.`;
        toast.error(`${lead} ${failureText(failures, stoppedEarly)}`);
      }
    } finally {
      runningRef.current = false;
      setRunning(false);
      setRunCount((count) => count + 1);
    }
  }

  // Counts read for a different set of DJs count as not loaded yet.
  const lore = loaded !== null && loaded.key === loadKey ? loaded.lore : null;
  const thinCount = lore === null ? 0 : thinDjs(personas, lore.counts).length;
  const allUnread = lore !== null && personas.length > 0 && lore.counts.size === 0;
  const everyDjHasLore = lore !== null && !allUnread && lore.unreadCount === 0 && thinCount === 0;

  let label = "Draft lore for thin DJs";
  if (running) label = "Drafting lore…";
  else if (allUnread) label = "Could not read lore";
  else if (everyDjHasLore) label = "Every DJ has lore";

  return (
    <Button
      type="button"
      variant="secondary"
      disabled={running || lore === null || allUnread || everyDjHasLore}
      onClick={() => void draft()}
    >
      {label}
    </Button>
  );
}
