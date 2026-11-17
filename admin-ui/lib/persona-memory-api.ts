// Client-side wire shapes + fetchers for a DJ's memory (SPEC F218.24, STORY-540, PLAN T707).
// Mirrors PersonaMemoryController (src/GenWave.Host/Api/PersonaMemoryController.cs). Browser
// fetches go through the same-origin /api rewrite. A failed call throws a MemoryApiError whose
// message is the server's own plain-words ProblemDetails detail.

import { apiFetch, isApiFetchResponseError } from "@/lib/api-fetch";
import { readErrorMessage } from "@/lib/problem-details";

export type MemoryKind = "lore" | "episode" | "life";
export type MemorySource = "authored" | "drafted" | "extracted" | "accrued";

/** `MemoryText.MaxChars` on the server — the API rejects anything longer. */
export const MEMORY_TEXT_MAX_CHARS = 200;

/** Mirrors `PersonaMemoryRowDto`. */
export interface PersonaMemoryRow {
  id: number;
  kind: MemoryKind;
  source: MemorySource;
  text: string;
  airedCount: number;
  /** Offers to the DJ in the last 28 days (server constant CallbackStore.OfferRetentionDays). */
  askedCount: number;
  lastAiredAt: string | null;
  createdAt: string;
  approvedAt: string | null;
  forgottenAt: string | null;
  pending: boolean;
  forgotten: boolean;
}

/** `PATCH` body — send exactly one field. `approved` can only be `true`. */
export type PersonaMemoryChange = { text: string } | { approved: true } | { forgotten: boolean };

export class MemoryApiError extends Error {}

const JSON_HEADERS = { "Content-Type": "application/json" };

async function send(url: string, init: RequestInit): Promise<Response> {
  try {
    return await apiFetch(url, { credentials: "include", ...init });
  } catch (err) {
    if (isApiFetchResponseError(err)) throw new MemoryApiError(await readErrorMessage(err));
    throw new MemoryApiError("Network error — check your connection.");
  }
}

function memoryUrl(personaId: number, suffix = ""): string {
  return `/api/personas/${personaId}/memory${suffix}`;
}

export async function fetchPersonaMemory(personaId: number): Promise<PersonaMemoryRow[]> {
  const response = await send(memoryUrl(personaId), { cache: "no-store" });
  return (await response.json()) as PersonaMemoryRow[];
}

export async function addLore(personaId: number, text: string): Promise<PersonaMemoryRow> {
  const response = await send(memoryUrl(personaId), {
    method: "POST",
    headers: JSON_HEADERS,
    body: JSON.stringify({ text }),
  });
  return (await response.json()) as PersonaMemoryRow;
}

export async function suggestLore(personaId: number): Promise<PersonaMemoryRow[]> {
  const response = await send(memoryUrl(personaId, "/suggest"), { method: "POST" });
  return (await response.json()) as PersonaMemoryRow[];
}

export async function changeMemoryRow(
  personaId: number,
  rowId: number,
  change: PersonaMemoryChange
): Promise<PersonaMemoryRow> {
  const response = await send(memoryUrl(personaId, `/${rowId}`), {
    method: "PATCH",
    headers: JSON_HEADERS,
    body: JSON.stringify(change),
  });
  return (await response.json()) as PersonaMemoryRow;
}

export async function deleteMemoryRow(personaId: number, rowId: number): Promise<void> {
  await send(memoryUrl(personaId, `/${rowId}`), { method: "DELETE" });
}
