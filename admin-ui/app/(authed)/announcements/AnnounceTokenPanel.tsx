"use client";

import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import { formatUpSince } from "@/lib/format-clock";
import {
  generateAnnounceToken,
  revokeAnnounceToken,
  type AnnounceTokenStatusDto,
} from "@/lib/announcements-api";

export interface AnnounceTokenPanelProps {
  status: AnnounceTokenStatusDto;
  /** Re-reads GET /api/announcements/token/status after a generate/regenerate/revoke — the panel
   * never mutates `status` itself, mirroring SafeContentClient's own "parent owns the fetched state,
   * child asks it to refresh" split. */
  onStatusChanged: () => void;
  /** Test-only injection point for the last-used timestamp formatter; production omits this and
   * gets the browser's local zone (mirrors AnnouncementHistoryList's own `timeZone` prop). */
  timeZone?: string;
}

/**
 * The Announcements page's token section (SPEC F145.3/F146.3, STORY-361, PLAN T344) —
 * generate/regenerate with REVEAL-ONCE, revoke with confirm, a last-used indicator. The revealed
 * plaintext lives ONLY in this component's own React state (`revealedToken` below): never written to
 * `localStorage`/`sessionStorage`, never placed in a URL (no query string, no route), and never
 * re-derivable after navigation — a route change or a page reload unmounts this component and the
 * state is gone for good, the same "gone once you look away" contract the server's own reveal-once
 * response already establishes one hop earlier (T340 review's binding UI contract).
 */
export function AnnounceTokenPanel({ status, onStatusChanged, timeZone }: AnnounceTokenPanelProps): ReactNode {
  const [revealedToken, setRevealedToken] = useState<string | null>(null);
  const [isPending, setIsPending] = useState(false);
  const confirm = useConfirm();

  async function handleGenerate(): Promise<void> {
    setIsPending(true);
    const outcome = await generateAnnounceToken();
    setIsPending(false);

    if (!outcome.ok) {
      toast.error(outcome.detail);
      return;
    }
    setRevealedToken(outcome.token);
    onStatusChanged();
  }

  async function handleCopy(): Promise<void> {
    if (revealedToken === null) return;
    try {
      await navigator.clipboard.writeText(revealedToken);
      toast.success("Token copied to clipboard.");
    } catch {
      toast.error("Couldn't copy — select and copy the token manually.");
    }
  }

  async function handleRevoke(): Promise<void> {
    const confirmed = await confirm({
      title: "Revoke the announce token?",
      consequence:
        "Any Home Assistant integration or other automation using this token stops working immediately. You can generate a new one any time.",
      confirmLabel: "Revoke",
      destructive: true,
    });
    if (!confirmed) return;

    setIsPending(true);
    const revoked = await revokeAnnounceToken();
    setIsPending(false);

    if (!revoked) {
      toast.error("Couldn't revoke the token — try again.");
      return;
    }
    setRevealedToken(null);
    toast.success("Token revoked.");
    onStatusChanged();
  }

  return (
    <section
      aria-label="Announce token"
      className="rounded-[6px] border border-line bg-surface p-5"
    >
      <h2 className="font-display text-[1.1rem] text-ink">Announce token</h2>
      <p className="mt-1 text-[0.82rem] text-mute">
        Lets Home Assistant (or any other automation) send announcements without an admin session.
      </p>

      {revealedToken !== null && (
        <div className="mt-4 rounded-[6px] border border-accent bg-surface-2 p-3">
          <p className="text-[0.75rem] font-semibold uppercase tracking-[0.1em] text-accent-2">
            Shown once — copy it now
          </p>
          <p className="mt-1.5 break-all font-mono text-[0.85rem] text-ink">{revealedToken}</p>
          <p className="mt-1.5 text-[0.75rem] text-mute">
            This won&rsquo;t be shown again. If you lose it, generate a new one.
          </p>
          <Button variant="secondary" className="mt-2" onClick={() => { void handleCopy(); }}>
            Copy to clipboard
          </Button>
        </div>
      )}

      <dl className="mt-4 flex flex-col gap-1 text-[0.85rem]">
        <div className="flex items-center gap-2">
          <dt className="text-mute">Status</dt>
          <dd className="text-ink">{status.hasToken ? "Active" : "No token yet"}</dd>
        </div>
        <div className="flex items-center gap-2">
          <dt className="text-mute">Last used</dt>
          <dd className="text-ink">
            {status.lastUsedAt !== null ? formatUpSince(status.lastUsedAt, { timeZone }) : "Never"}
          </dd>
        </div>
      </dl>

      <div className="mt-4 flex gap-2">
        <Button onClick={() => { void handleGenerate(); }} disabled={isPending}>
          {status.hasToken ? "Regenerate" : "Generate"}
        </Button>
        {status.hasToken && (
          <Button variant="secondary" onClick={() => { void handleRevoke(); }} disabled={isPending}>
            Revoke
          </Button>
        )}
      </div>
    </section>
  );
}
