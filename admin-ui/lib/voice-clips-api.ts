// Client-side wire shapes + calls for the voice clip family (SPEC F219.12-F219.14, PLAN T758):
// GET/POST api/voices/clips and DELETE api/voices/clips/{slug} (VoiceClipController). Browser
// fetches go through the Next.js same-origin rewrite and the 401 contract in api-fetch.ts.

import { apiFetch, isApiFetchResponseError } from "@/lib/api-fetch";
import { readErrorMessage } from "@/lib/problem-details";

/** One row of `GET /api/voices/clips` (VoiceClipDto). `source` is `starter` for a shipped clip. */
export interface VoiceClipDto {
  slug: string;
  label: string;
  durationMs: number;
  source: string;
  credit: string | null;
  createdAt: string;
}

/** The server's own upload ceiling (VoiceClipLimits.MaxFileBytes), checked here first so a huge
 * file is never sent just to be refused. */
export const MAX_CLIP_BYTES = 10 * 1024 * 1024;

/** Same sentence the server gives for the same refusal. */
export const CLIP_TOO_LARGE_MESSAGE = "The file is larger than 10 MiB.";

export type VoiceClipOutcome<T> = { ok: true; value: T } | { ok: false; message: string };

/** True for a clip that shipped with the station rather than being uploaded. */
export function isStarterClip(clip: VoiceClipDto): boolean {
  return clip.source === "starter";
}

async function failure(err: unknown, fallback: string): Promise<{ ok: false; message: string }> {
  if (isApiFetchResponseError(err)) {
    return { ok: false, message: await readErrorMessage(err) };
  }
  return { ok: false, message: fallback };
}

/** Uploads one recording. A refusal (400) resolves to the server's own reason. */
export async function uploadVoiceClip(label: string, file: File): Promise<VoiceClipOutcome<VoiceClipDto>> {
  const form = new FormData();
  form.append("label", label);
  form.append("file", file);
  try {
    // No Content-Type header: the browser sets the multipart boundary itself.
    const response = await apiFetch("/api/voices/clips", { method: "POST", body: form, credentials: "include" });
    return { ok: true, value: (await response.json()) as VoiceClipDto };
  } catch (err) {
    return failure(err, "The upload failed. Try again.");
  }
}

/** Deletes one clip. A 409 resolves to the server's sentence naming the DJs that still use it. */
export async function deleteVoiceClip(slug: string): Promise<VoiceClipOutcome<null>> {
  try {
    await apiFetch(`/api/voices/clips/${encodeURIComponent(slug)}`, { method: "DELETE", credentials: "include" });
    return { ok: true, value: null };
  } catch (err) {
    return failure(err, "The clip could not be deleted. Try again.");
  }
}
