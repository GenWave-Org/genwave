"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { cn } from "@/lib/utils";
import { sendAnnouncement } from "@/lib/announcements-api";

// SPEC F143.1/F143.4/F146.1 — the fixed laws this form mirrors for immediate feedback; the server's
// own 400 stays the single source of truth (this is a courtesy, never a duplicate enforcement).
const MAX_MESSAGE_CHARS = 280;
const MIN_TTL_SECONDS = 60;
const MAX_TTL_SECONDS = 3600;
const DEFAULT_TTL_SECONDS = 900;

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

export interface AnnouncementComposerProps {
  /** SPEC F145.1/F146.3 — while true, the send is replaced with the public-mode notice instead of
   * offering a control that will 403. */
  spectatorMode: boolean;
  /** Fires after a genuinely accepted send (the id `AnnouncementAcceptedDto` carries) — the parent
   * owns turning that into a history refresh, mirroring SafeContentClient's own "child reports, parent
   * refetches" split (this endpoint returns id-only, so there is no created row to prepend locally). */
  onSent: (id: number) => void;
}

/**
 * The Announcements page's send form (SPEC F146.1, STORY-361 AC1) — message box with a live 280
 * counter, verbatim toggle, optional TTL override, and the SpectatorMode notice (F146.3, AC4) that
 * replaces the send entirely rather than offering a control that would 403. POSTs through
 * `sendAnnouncement` — the same `POST /api/announcements` endpoint every other announcement source
 * uses; this form owns no parallel write path.
 */
export function AnnouncementComposer({ spectatorMode, onSent }: AnnouncementComposerProps): ReactNode {
  const [message, setMessage] = useState("");
  const [verbatim, setVerbatim] = useState(false);
  const [ttlOverride, setTtlOverride] = useState("");
  const [isPending, setIsPending] = useState(false);
  const [fieldError, setFieldError] = useState<string | null>(null);

  if (spectatorMode) {
    return (
      <section aria-label="Announcements disabled" className="rounded-[6px] border border-line bg-surface p-5">
        <h2 className="font-display text-[1.1rem] text-ink">Send an announcement</h2>
        <p className="mt-2 text-[0.85rem] text-mute">
          Announcements are off while the station is public — a public stream never carries the
          house&rsquo;s events. Turn off Spectator Mode in Settings to send one.
        </p>
      </section>
    );
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    setFieldError(null);

    if (message.trim().length === 0) {
      setFieldError("Type a message first.");
      return;
    }

    let ttlSeconds: number | undefined;
    if (ttlOverride.trim() !== "") {
      const parsed = Number(ttlOverride);
      if (!Number.isFinite(parsed) || parsed < MIN_TTL_SECONDS || parsed > MAX_TTL_SECONDS) {
        setFieldError(`Expires-after must be between ${MIN_TTL_SECONDS} and ${MAX_TTL_SECONDS} seconds.`);
        return;
      }
      ttlSeconds = parsed;
    }

    setIsPending(true);
    const outcome = await sendAnnouncement({ message: message.trim(), verbatim, ttlSeconds });
    setIsPending(false);

    if (!outcome.ok) {
      setFieldError(outcome.detail);
      toast.error(outcome.detail);
      return;
    }

    toast.success("Announcement sent — pending its next break.");
    setMessage("");
    setVerbatim(false);
    setTtlOverride("");
    onSent(outcome.id);
  }

  const remaining = MAX_MESSAGE_CHARS - message.length;

  return (
    <section aria-label="Send an announcement" className="rounded-[6px] border border-line bg-surface p-5">
      <h2 className="font-display text-[1.1rem] text-ink">Send an announcement</h2>

      {fieldError !== null && (
        <p role="alert" aria-live="assertive" className="mt-3 text-[0.82rem] text-danger">
          {fieldError}
        </p>
      )}

      <form onSubmit={(e) => { void handleSubmit(e); }} className="mt-4 flex flex-col gap-4">
        <div className="flex flex-col gap-1.5">
          <div className="flex items-baseline justify-between">
            <label htmlFor="announcement-message" className={FIELD_LABEL_CLASSES}>
              Message
            </label>
            <span
              className={cn("text-[0.75rem] tabular-nums", remaining < 0 ? "text-danger" : "text-mute")}
              aria-live="polite"
            >
              {remaining}
            </span>
          </div>
          <textarea
            id="announcement-message"
            name="message"
            value={message}
            onChange={(e) => setMessage(e.currentTarget.value)}
            disabled={isPending}
            rows={3}
            placeholder="Dinner's ready — come and get it before it's gone."
            className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
          />
        </div>

        <label className="flex items-center gap-2 text-[0.85rem] text-ink">
          <input
            id="announcement-verbatim"
            name="verbatim"
            type="checkbox"
            checked={verbatim}
            onChange={(e) => setVerbatim(e.currentTarget.checked)}
            disabled={isPending}
            className="h-4 w-4 disabled:opacity-50"
          />
          Speak it exactly as written (skip DJ flavor)
        </label>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="announcement-ttl" className={FIELD_LABEL_CLASSES}>
            Expires after (seconds, optional)
          </label>
          <input
            id="announcement-ttl"
            name="ttlSeconds"
            type="number"
            min={MIN_TTL_SECONDS}
            max={MAX_TTL_SECONDS}
            placeholder={String(DEFAULT_TTL_SECONDS)}
            value={ttlOverride}
            onChange={(e) => setTtlOverride(e.currentTarget.value)}
            disabled={isPending}
            className={`${FIELD_INPUT_CLASSES} w-40`}
          />
        </div>

        <div>
          <Button type="submit" disabled={isPending || message.trim().length === 0}>
            {isPending ? "Sending…" : "Send"}
          </Button>
        </div>
      </form>
    </section>
  );
}
