"use client";

import { useCallback } from "react";
import { toast } from "@/components/ui/toast";

/** A row this hook can PATCH — the id plus the bare version (Postgres xmin, no `W/"…"` wrapper)
 * used to build the `If-Match` header. */
export interface RowPatchTarget {
  mediaId: string;
  version: string;
}

/** Failure buckets the hook classifies a non-2xx/network outcome into (SPEC F31.3). Anything the
 * backend returns that isn't one of these (e.g. a 400 body-validation error) falls into
 * `"unknown"` — callers with a site-specific case for a code outside this set handle it via
 * `describeFailure`. */
export type RowPatchFailureKind =
  | "unauthorized"
  | "forbidden"
  | "not-found"
  | "conflict"
  | "unsupported-media-type"
  | "network"
  | "unknown";

export interface RowPatchSuccess {
  ok: true;
  /**
   * The row's fresh bare version, parsed from the response `ETag` (F31.1). Callers MUST fold
   * this back into whatever they hold as that row's version — that's what makes an immediate
   * second PATCH on the same row succeed (the gitea-#181 double-toggle repro, F31.2).
   */
  version: string;
  /** Parsed JSON body, or `null` for a bodyless 204 / a body that failed to parse. */
  body: unknown;
}

export interface RowPatchFailure {
  ok: false;
  kind: RowPatchFailureKind;
  /** HTTP status, or `null` when the request never got a response (network failure). */
  status: number | null;
}

export type RowPatchOutcome = RowPatchSuccess | RowPatchFailure;

function classifyStatus(status: number): RowPatchFailureKind {
  switch (status) {
    case 401:
      return "unauthorized";
    case 403:
      return "forbidden";
    case 404:
      return "not-found";
    case 409:
    case 412:
      return "conflict";
    case 415:
      return "unsupported-media-type";
    default:
      return "unknown";
  }
}

function defaultFailureMessage(failure: RowPatchFailure): string {
  switch (failure.kind) {
    case "unauthorized":
      return "Your session has expired — sign in again.";
    case "forbidden":
      return "You don't have permission to make this change.";
    case "not-found":
      return "This track no longer exists.";
    case "conflict":
      return "This track changed elsewhere — refreshed so you can retry.";
    case "unsupported-media-type":
      return "That request wasn't understood — this is a bug, not you.";
    case "network":
      return "Network error — check your connection";
    case "unknown":
      return `Unexpected error (${failure.status ?? "?"})`;
  }
}

/**
 * Strips a `W/"<token>"` (RFC 7232 weak) or plain `"<token>"` ETag wrapper down to the bare
 * version token — mirrors the backend's `MediaController.StripETagWrapper`. Exported so a site
 * seeded with a full ETag from a server-rendered GET (EditTrackForm, MoveToLibraryAction) can
 * derive the bare starting version this hook's `RowPatchTarget.version` expects.
 */
export function stripWeakETag(etag: string): string {
  const tag = etag.trim();
  if (tag.startsWith('W/"') && tag.endsWith('"')) return tag.slice(3, -1);
  if (tag.startsWith('"') && tag.endsWith('"')) return tag.slice(1, -1);
  return tag;
}

export interface UseRowPatchOptions {
  /**
   * Called after a conflict outcome (409/412) so the caller can refresh whatever it holds for
   * this row, so an immediate retry is possible (F31.3 AC5). Omit when the caller's own refresh
   * already covers it on a different cadence — e.g. CatalogToolbar's selection-mode summary
   * refresh reconciles every row (failed ones included) once any row in the batch succeeds; it
   * deliberately skips the refresh when every row fails, so a single row's conflict there does
   * not trigger one (Q7 review, preserved as-is).
   */
  onConflict?: (target: RowPatchTarget) => void;
  /**
   * Set `false` to suppress the hook's own failure toast — for a caller that aggregates several
   * PATCH outcomes into one summary toast itself (CatalogToolbar's selection mode). Every other
   * site has exactly one row in flight per call, so the hook's toast is the operator's only
   * feedback and this defaults to `true`.
   */
  notify?: boolean;
  /**
   * Supplies site-specific wording for a failure outcome — return a string to toast instead of
   * the hook's default copy, or `undefined` to fall back to the default. Lets a site keep an
   * established message (e.g. "your edits are unsaved") or cover a status this hook doesn't
   * classify on its own (e.g. the reassign PATCH's 400 unknown-library). Ignored when `notify`
   * is `false`.
   */
  describeFailure?: (failure: RowPatchFailure) => string | undefined;
}

export interface UseRowPatchResult {
  /**
   * PATCHes `/api/media/{target.mediaId}` with `body`, `Content-Type: application/json` and
   * `If-Match: W/"<target.version>"` — the one wire call every row-edit site makes. Never throws;
   * a thrown/rejected fetch resolves to a `{ ok: false, kind: "network" }` outcome like any other
   * failure.
   */
  patchRow: (target: RowPatchTarget, body: Record<string, unknown>) => Promise<RowPatchOutcome>;
}

/**
 * Shared row-PATCH hook (SPEC F31.2–F31.3, STORY-104, closes gitea-#181's UI half with R8). The one
 * place that builds the `If-Match` PATCH, reads the response's fresh `ETag` back into a bare
 * version, classifies failures, and toasts them (F28.9) — no site builds this fetch call inline.
 *
 * The hook holds no row state of its own: every site already has somewhere to keep a row's
 * version (component state or a server-rendered prop), so on success the caller folds
 * `outcome.version` back into that. This is a deliberate seam (not an oversight) — a hook-owned
 * cache would either duplicate each site's state or force one shape on all four, and the sites
 * disagree today (a list of rows, a single form, a fan-out over a selection).
 */
export function useRowPatch(options: UseRowPatchOptions = {}): UseRowPatchResult {
  const { onConflict, notify = true, describeFailure } = options;

  const patchRow = useCallback(
    async (target: RowPatchTarget, body: Record<string, unknown>): Promise<RowPatchOutcome> => {
      let response: Response;
      try {
        response = await fetch(`/api/media/${target.mediaId}`, {
          method: "PATCH",
          headers: {
            "Content-Type": "application/json",
            "If-Match": `W/"${target.version}"`,
          },
          body: JSON.stringify(body),
        });
      } catch {
        const failure: RowPatchFailure = { ok: false, kind: "network", status: null };
        if (notify) toast.error(describeFailure?.(failure) ?? defaultFailureMessage(failure));
        return failure;
      }

      if (response.ok) {
        const etagHeader = response.headers.get("etag");
        const version = etagHeader !== null ? stripWeakETag(etagHeader) : target.version;
        const responseBody: unknown = await response.json().catch(() => null);
        return { ok: true, version, body: responseBody };
      }

      const failure: RowPatchFailure = {
        ok: false,
        kind: classifyStatus(response.status),
        status: response.status,
      };
      if (notify) toast.error(describeFailure?.(failure) ?? defaultFailureMessage(failure));
      if (failure.kind === "conflict") onConflict?.(target);
      return failure;
    },
    [notify, onConflict, describeFailure]
  );

  return { patchRow };
}
