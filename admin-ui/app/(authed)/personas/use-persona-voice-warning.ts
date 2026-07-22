"use client";

import { useEffect, useState } from "react";
import type { VoiceListState } from "@/lib/use-voice-list";
import { parsePersonaCardPreview } from "./persona-card";
import { personaSlug } from "./persona-slug";
import type { PersonaDto } from "./types";

export interface PersonaVoiceWarning {
  personaId: number;
  /** The card's authored voice id that this station doesn't have (SPEC F79.4). */
  voiceId: string;
}

/**
 * Derives the F79.4/F79.5 import-warning state ON READ, with no dedicated backend field for it
 * (T67 review: no new schema needed) — by re-fetching the candidate persona's own export card
 * (`GET /api/personas/{slug}/export`, the SAME endpoint the Export button downloads, just
 * consumed as JSON text instead of navigated to) and comparing its authored `voice.voiceId`
 * against the live engine voice list.
 *
 * A persona only ever reaches this state via import: the ordinary create/edit form always keeps
 * `persona.voice` and the card's `voice.voiceId` in sync (`PersonaController.ToDraft` round-trips
 * the same string both ways), so `persona.voice === ""` while the card still names a real voice
 * can only mean import degraded it (`ResolveVoiceAsync`'s unresolved-voice path). Fails CLOSED
 * toward "no warning" — the same posture as that backend method — whenever the export fetch
 * fails, the card doesn't parse, or the live voice list itself couldn't be loaded: an unreachable
 * engine must never manufacture a false alarm.
 *
 * `candidate` is null outside the edit form (SPEC F79.5 links the warning TO the voice picker,
 * which only renders there) — passing null clears any prior warning immediately.
 */
export function usePersonaVoiceWarning(
  candidate: PersonaDto | null,
  voiceList: VoiceListState
): PersonaVoiceWarning | null {
  const [warning, setWarning] = useState<PersonaVoiceWarning | null>(null);

  useEffect(() => {
    if (candidate === null || candidate.voice !== "" || voiceList.kind !== "loaded") {
      setWarning(null);
      return;
    }

    let cancelled = false;
    const { id, name } = candidate;
    const voices = voiceList.voices;

    async function check(): Promise<void> {
      try {
        const resp = await fetch(`/api/personas/${personaSlug(name)}/export`);
        if (!resp.ok) {
          if (!cancelled) setWarning(null);
          return;
        }
        const preview = parsePersonaCardPreview(await resp.text());
        if (preview !== null && preview.voiceId !== "" && !voices.includes(preview.voiceId)) {
          if (!cancelled) setWarning({ personaId: id, voiceId: preview.voiceId });
        } else if (!cancelled) {
          setWarning(null);
        }
      } catch {
        if (!cancelled) setWarning(null);
      }
    }

    void check();
    return () => {
      cancelled = true;
    };
  }, [candidate, voiceList]);

  return warning;
}
