"use client";

import { useState, type FormEvent, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { toast } from "@/components/ui/toast";
import { stripWeakETag, useRowPatch } from "@/lib/use-row-patch";

export interface EditableTrackFields {
  title: string | null;
  artist: string | null;
  album: string | null;
  genre: string | null;
  year: number | null;
  eligible: boolean;
}

interface EditTrackFormProps {
  mediaId: string;
  initialValues: EditableTrackFields;
  /** Weak ETag from the GET /api/media/{id} response — forwarded verbatim as If-Match. */
  etag: string;
}

type SaveStatus = "idle" | "saving" | "conflict";

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";

export function EditTrackForm({
  mediaId,
  initialValues,
  etag,
}: EditTrackFormProps): ReactNode {
  const router = useRouter();
  const [values, setValues] = useState<EditableTrackFields>(initialValues);
  const [status, setStatus] = useState<SaveStatus>("idle");
  // Bare version derived from the server-rendered ETag prop, then kept current from each PATCH
  // response's fresh ETag (SPEC F31.2) — an immediate second save no longer depends on
  // router.refresh() completing first.
  const [version, setVersion] = useState<string>(() => stripWeakETag(etag));
  const { patchRow } = useRowPatch({
    describeFailure: (failure) =>
      failure.kind === "conflict" ? "This track changed elsewhere — your edits are unsaved." : undefined,
  });

  function handleStringChange(
    field: keyof Pick<EditableTrackFields, "title" | "artist" | "album" | "genre">
  ): (e: React.ChangeEvent<HTMLInputElement>) => void {
    return (e) => {
      const val = e.currentTarget.value;
      setValues((prev) => ({ ...prev, [field]: val === "" ? null : val }));
    };
  }

  function handleYearChange(e: React.ChangeEvent<HTMLInputElement>): void {
    const raw = e.currentTarget.value;
    const parsed = raw === "" ? null : parseInt(raw, 10);
    setValues((prev) => ({ ...prev, year: isNaN(parsed ?? NaN) ? null : parsed }));
  }

  function handleEligibleChange(e: React.ChangeEvent<HTMLInputElement>): void {
    setValues((prev) => ({ ...prev, eligible: e.currentTarget.checked }));
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    setStatus("saving");

    const patch: Record<string, string | number | boolean | null> = {
      title: values.title,
      artist: values.artist,
      album: values.album,
      genre: values.genre,
      year: values.year,
      eligible: values.eligible,
    };

    const outcome = await patchRow({ mediaId, version }, patch);

    if (outcome.ok) {
      setVersion(outcome.version);
      toast.success("Saved.");
      setStatus("idle");
      // Also refreshes the rest of the page's server-rendered data (e.g. the details table
      // above this form), which the hook's returned version does not cover.
      router.refresh();
      return;
    }

    // The hook already toasted the outcome — this only drives the local UI state.
    setStatus(outcome.kind === "conflict" ? "conflict" : "idle");
  }

  const isPending = status === "saving";

  return (
    <form onSubmit={(e) => { void handleSubmit(e); }} className="flex max-w-md flex-col gap-4">
      {status === "conflict" && (
        <div
          role="alert"
          className="flex flex-wrap items-center justify-between gap-3 rounded-[6px] border border-danger bg-surface px-3 py-2 text-[0.82rem] text-danger"
        >
          <span>This track changed elsewhere — reload before saving again.</span>
          <Button type="button" variant="secondary" onClick={() => router.refresh()}>
            Reload
          </Button>
        </div>
      )}

      <div className="flex flex-col gap-1.5">
        <label htmlFor="edit-title" className={FIELD_LABEL_CLASSES}>Title</label>
        <input
          id="edit-title"
          name="title"
          type="text"
          value={values.title ?? ""}
          onChange={handleStringChange("title")}
          disabled={isPending}
          className={FIELD_INPUT_CLASSES}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="edit-artist" className={FIELD_LABEL_CLASSES}>Artist</label>
        <input
          id="edit-artist"
          name="artist"
          type="text"
          value={values.artist ?? ""}
          onChange={handleStringChange("artist")}
          disabled={isPending}
          className={FIELD_INPUT_CLASSES}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="edit-album" className={FIELD_LABEL_CLASSES}>Album</label>
        <input
          id="edit-album"
          name="album"
          type="text"
          value={values.album ?? ""}
          onChange={handleStringChange("album")}
          disabled={isPending}
          className={FIELD_INPUT_CLASSES}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="edit-genre" className={FIELD_LABEL_CLASSES}>Genre</label>
        <input
          id="edit-genre"
          name="genre"
          type="text"
          value={values.genre ?? ""}
          onChange={handleStringChange("genre")}
          disabled={isPending}
          className={FIELD_INPUT_CLASSES}
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="edit-year" className={FIELD_LABEL_CLASSES}>Year</label>
        <input
          id="edit-year"
          name="year"
          type="number"
          value={values.year ?? ""}
          onChange={handleYearChange}
          disabled={isPending}
          className={`${FIELD_INPUT_CLASSES} tabular-nums`}
        />
      </div>

      <div className="flex min-h-10 items-center gap-2">
        <input
          id="edit-eligible"
          name="eligible"
          type="checkbox"
          checked={values.eligible}
          onChange={handleEligibleChange}
          disabled={isPending}
        />
        <label htmlFor="edit-eligible" className="text-[0.85rem] text-ink">
          Eligible for playout
        </label>
      </div>

      <Button type="submit" disabled={isPending} className="self-start">
        {isPending ? "Saving…" : "Save"}
      </Button>
    </form>
  );
}
