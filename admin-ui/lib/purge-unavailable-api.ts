// Client-side fetcher for POST /api/media/purge-unavailable (gh-#113) — the explicit operator
// purge of long-unavailable rows. Browser fetches go through the Next.js same-origin rewrite
// (/api/* -> api:8080), never lib/api.ts's server-only apiGet — the broadcast-api convention.
//
// The endpoint's two-phase shape drives the UI flow: a dryRun call first (the confirm dialog must
// NAME the count before anything destructive fires), then the real call on confirm. A 409 is the
// server's mount-outage tripwire — more than half the library would be deleted — surfaced as its
// own `refused` variant so callers show the explanation instead of a generic failure.

import { readErrorMessage } from "@/lib/problem-details";

export type PurgeUnavailableResult =
  | { kind: "counted"; wouldDelete: number }
  | { kind: "purged"; deleted: number }
  | { kind: "refused"; message: string }
  | { kind: "error"; message: string };

interface PurgeUnavailableOptions {
  olderThanDays: number;
  dryRun: boolean;
}

interface DryRunBody {
  wouldDelete?: number;
}

interface PurgeBody {
  deleted?: number;
}

export async function purgeUnavailable(
  options: PurgeUnavailableOptions
): Promise<PurgeUnavailableResult> {
  let resp: Response;
  try {
    resp = await fetch("/api/media/purge-unavailable", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ olderThanDays: options.olderThanDays, dryRun: options.dryRun }),
    });
  } catch {
    return { kind: "error", message: "Network error — the purge request never reached the station." };
  }

  if (resp.status === 409) {
    return { kind: "refused", message: await readErrorMessage(resp) };
  }

  if (!resp.ok) {
    return { kind: "error", message: await readErrorMessage(resp) };
  }

  if (options.dryRun) {
    const body = (await resp.json()) as DryRunBody;
    return { kind: "counted", wouldDelete: body.wouldDelete ?? 0 };
  }

  const body = (await resp.json()) as PurgeBody;
  return { kind: "purged", deleted: body.deleted ?? 0 };
}
