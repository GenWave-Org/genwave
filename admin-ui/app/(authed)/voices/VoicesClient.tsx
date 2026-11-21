"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { useConfirm } from "@/components/ui/confirm-dialog";
import { toast } from "@/components/ui/toast";
import {
  CLIP_TOO_LARGE_MESSAGE,
  MAX_CLIP_BYTES,
  deleteVoiceClip,
  isStarterClip,
  uploadVoiceClip,
  type VoiceClipDto,
} from "@/lib/voice-clips-api";

export interface VoicesClientProps {
  initialClips: VoiceClipDto[];
}

function formatSeconds(durationMs: number): string {
  return `${(durationMs / 1000).toFixed(1)} s`;
}

/**
 * The Voices page body (SPEC F219.12-F219.14): clip list with credits, upload form, delete with a
 * confirm step. Clips are cloning-engine voices only — the per-engine voice list re-reads them on
 * its next fetch, so nothing else needs refreshing here.
 */
export function VoicesClient({ initialClips }: VoicesClientProps): ReactNode {
  const confirm = useConfirm();
  const [clips, setClips] = useState<VoiceClipDto[]>(initialClips);
  const [label, setLabel] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [fileInputKey, setFileInputKey] = useState(0);
  const [isSending, setIsSending] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [deletingSlugs, setDeletingSlugs] = useState<ReadonlySet<string>>(
    new Set(),
  );

  const canSend = label.trim() !== "" && file !== null && !isSending;

  function chooseFile(chosen: File | null): void {
    setFile(chosen);
    setUploadError(
      chosen !== null && chosen.size > MAX_CLIP_BYTES
        ? CLIP_TOO_LARGE_MESSAGE
        : null,
    );
  }

  async function handleUpload(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    if (file === null || label.trim() === "") return;
    if (file.size > MAX_CLIP_BYTES) {
      setUploadError(CLIP_TOO_LARGE_MESSAGE);
      return;
    }
    setIsSending(true);
    setUploadError(null);
    const outcome = await uploadVoiceClip(label.trim(), file);
    if (outcome.ok) {
      setClips((prev) => [...prev, outcome.value]);
      setLabel("");
      setFile(null);
      setFileInputKey((key) => key + 1);
      toast.success(`"${outcome.value.label}" added.`);
    } else {
      setUploadError(outcome.message);
    }
    setIsSending(false);
  }

  async function handleDelete(clip: VoiceClipDto): Promise<void> {
    const ok = await confirm({
      title: "Delete clip",
      consequence: `Delete "${clip.label}"? A DJ that uses this clip as a voice has to switch to another voice first.`,
      confirmLabel: "Delete",
      destructive: true,
    });
    if (!ok) return;

    setDeletingSlugs((prev) => new Set(prev).add(clip.slug));
    const outcome = await deleteVoiceClip(clip.slug);
    if (outcome.ok) {
      setClips((prev) => prev.filter((c) => c.slug !== clip.slug));
      toast.success(`"${clip.label}" deleted.`);
    } else {
      toast.error(outcome.message);
    }
    setDeletingSlugs((prev) => {
      const next = new Set(prev);
      next.delete(clip.slug);
      return next;
    });
  }

  return (
    <div className="flex flex-col gap-6">
      <section aria-label="Voice clips">
        {clips.length === 0 ? (
          <p className="text-[0.85rem] text-mute">There are no clips yet.</p>
        ) : (
          <ul className="divide-y divide-line rounded-[6px] border border-line bg-surface">
            {clips.map((clip) => {
              const starter = isStarterClip(clip);
              return (
                <li
                  key={clip.slug}
                  data-clip-slug={clip.slug}
                  className="flex items-start justify-between gap-3 px-4 py-3"
                >
                  <div className="min-w-0">
                    <p>
                      <span
                        data-testid="clip-label"
                        className="font-semibold text-ink"
                      >
                        {clip.label}
                      </span>{" "}
                      <span className="text-[0.8rem] text-mute">
                        {formatSeconds(clip.durationMs)} ·{" "}
                        {starter ? "Starter" : "Uploaded"}
                      </span>
                    </p>
                    {starter && clip.credit !== null && clip.credit !== "" && (
                      <p
                        data-testid="clip-credit"
                        className="mt-1 break-words text-[0.8rem] text-mute"
                      >
                        {clip.credit}
                      </p>
                    )}
                  </div>
                  <Button
                    type="button"
                    variant="secondary"
                    className="shrink-0"
                    aria-label={`Delete ${clip.label}`}
                    disabled={deletingSlugs.has(clip.slug)}
                    onClick={() => {
                      void handleDelete(clip);
                    }}
                  >
                    Delete
                  </Button>
                </li>
              );
            })}
          </ul>
        )}
      </section>

      <form
        aria-label="Upload a clip"
        onSubmit={(e) => {
          void handleUpload(e);
        }}
        className="flex flex-col gap-3"
      >
        <p className="text-[0.7rem] font-semibold uppercase tracking-[0.14em] text-accent-2">
          Add a clip
        </p>
        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="clip-label"
            className="text-[0.82rem] font-semibold text-mute"
          >
            Label
          </label>
          <input
            id="clip-label"
            type="text"
            maxLength={80}
            value={label}
            onChange={(e) => setLabel(e.currentTarget.value)}
            disabled={isSending}
            className="h-9 max-w-sm rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink"
          />
        </div>
        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="clip-file"
            className="text-[0.82rem] font-semibold text-mute"
          >
            Recording (WAV, FLAC or MP3, 5 to 20 seconds, up to 10 MiB)
          </label>
          <input
            key={fileInputKey}
            id="clip-file"
            type="file"
            accept=".wav,.flac,.mp3,audio/*"
            onChange={(e) => chooseFile(e.currentTarget.files?.[0] ?? null)}
            disabled={isSending}
            className="text-[0.85rem] text-ink"
          />
        </div>
        {uploadError !== null && (
          <p role="alert" className="text-[0.85rem] text-danger">
            {uploadError}
          </p>
        )}
        <div>
          <Button type="submit" disabled={!canSend}>
            {isSending ? "Uploading…" : "Upload clip"}
          </Button>
        </div>
      </form>
    </div>
  );
}
