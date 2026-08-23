// Client-side wire shapes + fetchers for the House Voice announcements family (SPEC F143/F145/F146,
// STORY-357/359/360/361, PLAN T344). POST/GET /api/announcements, GET/POST/DELETE
// /api/announcements/token[/status] — the SAME endpoint family AnnouncementsController serves; this
// module never invents a parallel write path (SPEC F146.1). Browser fetches go through the Next.js
// same-origin rewrite, mirroring broadcast-api.ts's own posture.

import { readErrorMessage } from "@/lib/problem-details";

/** The SPEC F143.2 total state machine's own wire text, lowercase — see
 * `GenWave.Core.Domain.AnnouncementHistoryEntry`'s own remarks for why the server never sends a
 * richer shape than this. */
export type AnnouncementState = "pending" | "claimed" | "aired" | "expired" | "declined";

/** One row of `GET /api/announcements` (SPEC F146.2) — the visible-decline/visible-expiry surface. */
export interface AnnouncementHistoryDto {
  id: number;
  message: string;
  verbatim: boolean;
  state: AnnouncementState;
  declineReason: string | null;
  collapseCount: number;
  createdAt: string;
  expiresAt: string;
  airedAt: string | null;
}

/** `GET /api/announcements` — newest first, every state, server-capped (50 default, 200 max). Never
 * throws: a network failure or non-2xx resolves to `null` so the page can render a quiet degrade
 * instead of an unhandled rejection (mirrors `usePoll`'s own never-throws contract one layer up). */
export async function fetchAnnouncementHistory(): Promise<AnnouncementHistoryDto[] | null> {
  try {
    const response = await fetch("/api/announcements", { credentials: "include", cache: "no-store" });
    if (!response.ok) return null;
    return (await response.json()) as AnnouncementHistoryDto[];
  } catch {
    return null;
  }
}

export interface SendAnnouncementInput {
  message: string;
  verbatim: boolean;
  /** Omitted uses the store's own 900s default; when present must already be in the SPEC F143.1
   * 60–3600s bound — this module never re-validates that bound, the server's own 400 is the single
   * source of truth the composer surfaces verbatim. */
  ttlSeconds?: number;
}

export type SendAnnouncementOutcome =
  | { ok: true; id: number }
  | { ok: false; status: number; detail: string };

/** `POST /api/announcements` (SPEC F143.1, F146.1) — the ONE write path this page (and every other
 * announcement source: the HA integration, F147.3) ever uses. Never throws: a network failure
 * resolves to the same classified-failure shape a non-2xx response does, so the composer always has
 * a `detail` string to show. */
export async function sendAnnouncement(input: SendAnnouncementInput): Promise<SendAnnouncementOutcome> {
  let response: Response;
  try {
    response = await fetch("/api/announcements", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(input),
    });
  } catch {
    return { ok: false, status: 0, detail: "Network error — check your connection." };
  }
  if (!response.ok) {
    return { ok: false, status: response.status, detail: await readErrorMessage(response) };
  }
  const body = (await response.json()) as { id: number };
  return { ok: true, id: body.id };
}

/** `GET /api/announcements/token/status` (SPEC F146.3) — presence + last-used only, NEVER the hash
 * or plaintext (the reveal-once contract, unchanged by this read). `null` on any failure — the token
 * panel degrades to "unknown" rather than assuming either state. */
export interface AnnounceTokenStatusDto {
  hasToken: boolean;
  lastUsedAt: string | null;
}

export async function fetchAnnounceTokenStatus(): Promise<AnnounceTokenStatusDto | null> {
  try {
    const response = await fetch("/api/announcements/token/status", {
      credentials: "include",
      cache: "no-store",
    });
    if (!response.ok) return null;
    return (await response.json()) as AnnounceTokenStatusDto;
  } catch {
    return null;
  }
}

export type GenerateAnnounceTokenOutcome = { ok: true; token: string } | { ok: false; detail: string };

/** `POST /api/announcements/token` (SPEC F145.3) — generate or regenerate; the ONLY response that
 * ever carries the plaintext (reveal-once). Session-only — this module never attaches a Bearer
 * header to this route, matching the server's own session-only door. */
export async function generateAnnounceToken(): Promise<GenerateAnnounceTokenOutcome> {
  let response: Response;
  try {
    response = await fetch("/api/announcements/token", { method: "POST", credentials: "include" });
  } catch {
    return { ok: false, detail: "Network error — check your connection." };
  }
  if (!response.ok) {
    return { ok: false, detail: await readErrorMessage(response) };
  }
  const body = (await response.json()) as { token: string };
  return { ok: true, token: body.token };
}

/** `DELETE /api/announcements/token` (SPEC F145.4) — fails closed on every Bearer request from the
 * very next call. Returns `true` on success; `false` never throws past this module. */
export async function revokeAnnounceToken(): Promise<boolean> {
  try {
    const response = await fetch("/api/announcements/token", { method: "DELETE", credentials: "include" });
    return response.ok;
  } catch {
    return false;
  }
}
