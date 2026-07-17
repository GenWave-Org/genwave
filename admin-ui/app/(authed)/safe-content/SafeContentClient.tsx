"use client";

import { useRef, useState, type ChangeEvent, type FormEvent, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { toast } from "@/components/ui/toast";
import type { LibraryDto } from "@/lib/library";
import { useRowPatch } from "@/lib/use-row-patch";
import { BedPicker } from "./BedPicker";
import type { BedCandidate } from "./BedPicker";
import { VoiceControl } from "./VoiceControl";

/** Shape of a GET /api/media row — the fields this page renders + the PATCH If-Match token. */
export interface SafeSegmentDto {
  mediaId: string;
  title: string | null;
  artist: string | null;
  state: string;
  durationMs: number | null;
  eligible: boolean;
  /** Row version (Postgres xmin) — the If-Match token for the eligibility PATCH (F18, W2). */
  version: string;
}

export interface SafeContentClientProps {
  /** Every library (GET /api/libraries, not scope-filtered, F20.1) — populates the target-library picker. */
  libraries: LibraryDto[];
  /** Resolved default target library id (the one named "safe" when present), or null if none exists. */
  initialLibraryId: number | null;
  /** Segments already in the target library, fetched server-side for the initial render. */
  initialSegments: SafeSegmentDto[];
  /** Whether the initial browse carried X-Out-Of-Scope: true (SPEC F23.2). */
  initialOutOfScope: boolean;
  /** Station:Safe:SeedMessage default text — pre-fills the Generate form's text field (F27.9). */
  defaultText: string;
  /** Default title pre-fill — "Please Stand By" (F27.3/F27.9). */
  defaultTitle: string;
}

/** ProblemDetails body shape returned on 400/502 from POST /api/safe-segments (F27.3). */
interface ProblemDetailsBody {
  detail?: string;
}

type GenerateStatus =
  | { kind: "idle" }
  | { kind: "pending" }
  | { kind: "error"; message: string };

function isProblemDetailsBody(raw: unknown): raw is ProblemDetailsBody {
  return typeof raw === "object" && raw !== null;
}

/** Extracts the ProblemDetails `detail` message from a failed response, falling back to a generic one. */
async function readErrorMessage(resp: Response): Promise<string> {
  try {
    const raw = (await resp.json()) as unknown;
    if (isProblemDetailsBody(raw) && typeof raw.detail === "string" && raw.detail !== "") {
      return raw.detail;
    }
  } catch {
    // malformed or empty body — fall through to the generic message
  }
  return `Unexpected error (${resp.status})`;
}

/** Fetches the target library's segments via the explicit library-id browse (F23.2). */
async function fetchSegments(
  libraryId: number
): Promise<{ segments: SafeSegmentDto[]; outOfScope: boolean } | null> {
  const resp = await fetch(`/api/media?library-id=${libraryId}&limit=200`);
  if (!resp.ok) return null;
  const segments = (await resp.json()) as SafeSegmentDto[];
  return { segments, outOfScope: resp.headers.get("X-Out-Of-Scope") === "true" };
}

const FIELD_LABEL_CLASSES = "text-[0.82rem] font-semibold text-mute";
const FIELD_INPUT_CLASSES =
  "h-9 rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";
const HEADER_CELL = "py-2 pr-3 text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";

/**
 * Client half of the "Safe content" page (SPEC F27.9/F28.9–F28.10, STORY-081/STORY-092). Owns
 * the Generate form (text, title, voice, optional bed, target library) and the target library's
 * segment list with an inline eligibility toggle. Consumes only the shipped endpoints: POST
 * /api/safe-segments (F27.3), GET /api/media (F15.4/F23.2), and PATCH /api/media/{id} (F18) — no
 * invented contracts. The Wireless restyle (Q10) is presentation-only: disabled-while-rendering,
 * the 400/502 inline-detail re-enable, and the wire body shape are unchanged from P8/P6.
 *
 * The eligibility toggle's PATCH runs through the shared row-PATCH hook (SPEC F31.2–F31.3,
 * STORY-104, gitea-#181): a success folds the response ETag back into the segment's cached `version`
 * (the gitea-#181 double-toggle repro — an immediate second toggle now succeeds instead of 409ing on
 * a stale version), and a failure toasts instead of silently leaving the row untouched.
 */
export function SafeContentClient({
  libraries,
  initialLibraryId,
  initialSegments,
  initialOutOfScope,
  defaultText,
  defaultTitle,
}: SafeContentClientProps): ReactNode {
  const [libraryId, setLibraryId] = useState<number | null>(initialLibraryId);
  const [segments, setSegments] = useState<SafeSegmentDto[]>(initialSegments);
  const [outOfScope, setOutOfScope] = useState<boolean>(initialOutOfScope);

  const [text, setText] = useState(defaultText);
  const [title, setTitle] = useState(defaultTitle);
  const [voice, setVoice] = useState("");
  const [bed, setBed] = useState<BedCandidate | null>(null);
  const [status, setStatus] = useState<GenerateStatus>({ kind: "idle" });
  const textFieldRef = useRef<HTMLTextAreaElement | null>(null);

  /** Re-fetches the target library's segment list — used both on an explicit library switch and
   * as the shared hook's conflict recovery (SPEC F31.3): a 409/412 on the eligibility PATCH means
   * this row's cached version is stale, so the whole list is refreshed to hand the operator a
   * current version for an immediate retry. */
  function refreshSegments(id: number): void {
    void fetchSegments(id).then((result) => {
      if (result === null) return;
      setSegments(result.segments);
      setOutOfScope(result.outOfScope);
    });
  }

  const { patchRow } = useRowPatch({
    onConflict: () => {
      if (libraryId !== null) refreshSegments(libraryId);
    },
  });

  const isPending = status.kind === "pending";

  /** EmptyState CTA target (SPEC F28.10: Safe content empty → Generate) — the form is already
   * on this page, so "go generate" means focus its first field rather than navigate away. */
  function focusGenerateForm(): void {
    textFieldRef.current?.focus();
  }

  function handleLibraryChange(e: ChangeEvent<HTMLSelectElement>): void {
    const id = parseInt(e.currentTarget.value, 10);
    if (isNaN(id)) return;
    setLibraryId(id);
    refreshSegments(id);
  }

  async function handleGenerate(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();

    if (libraryId === null) {
      setStatus({ kind: "error", message: "Select a target library first." });
      return;
    }

    setStatus({ kind: "pending" });

    const body: Record<string, unknown> = { text, libraryId };
    if (title.trim() !== "") body["title"] = title;
    if (voice.trim() !== "") body["voice"] = voice;
    if (bed !== null) body["bedMediaId"] = bed.mediaId;

    try {
      const resp = await fetch("/api/safe-segments", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });

      if (resp.status === 201) {
        const created = (await resp.json()) as SafeSegmentDto;
        setSegments((prev) => [created, ...prev]);
        setBed(null);
        setStatus({ kind: "idle" });
        toast.success("Segment generated.");
        return;
      }

      // 400/502 (F27.3) — re-enable the form with the inline error instead of leaving it stuck
      // disabled (the K5 stuck-Saving… regression class); also toast so the failure is visible
      // even if the operator has scrolled past the form (F28.9).
      const message = await readErrorMessage(resp);
      setStatus({ kind: "error", message });
      toast.error(message);
    } catch {
      const message = "Network error — check your connection";
      setStatus({ kind: "error", message });
      toast.error(message);
    }
  }

  async function handleToggleEligible(segment: SafeSegmentDto): Promise<void> {
    const nextEligible = !segment.eligible;
    const outcome = await patchRow(
      { mediaId: segment.mediaId, version: segment.version },
      { eligible: nextEligible }
    );
    if (!outcome.ok) return; // the hook already toasted; leave the row's displayed state as-is

    setSegments((prev) =>
      prev.map((s) =>
        s.mediaId === segment.mediaId ? { ...s, eligible: nextEligible, version: outcome.version } : s
      )
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <section
        aria-label="Generate safe segment"
        className="rounded-[6px] border border-line bg-surface p-5"
      >
        <h2 className="font-display text-[1.1rem] text-ink">Generate</h2>

        {status.kind === "error" && (
          <p role="alert" aria-live="assertive" className="mt-3 text-[0.82rem] text-danger">
            {status.message}
          </p>
        )}

        <form onSubmit={(e) => { void handleGenerate(e); }} className="mt-4 flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <label htmlFor="safe-text" className={FIELD_LABEL_CLASSES}>Text</label>
            <textarea
              id="safe-text"
              name="text"
              ref={textFieldRef}
              value={text}
              onChange={(e) => setText(e.currentTarget.value)}
              disabled={isPending}
              rows={4}
              className={`${FIELD_INPUT_CLASSES} resize-y py-2`}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label htmlFor="safe-title" className={FIELD_LABEL_CLASSES}>Title</label>
            <input
              id="safe-title"
              name="title"
              type="text"
              value={title}
              onChange={(e) => setTitle(e.currentTarget.value)}
              disabled={isPending}
              className={FIELD_INPUT_CLASSES}
            />
          </div>

          <VoiceControl value={voice} onChange={setVoice} disabled={isPending} />

          <div className="flex flex-col gap-1.5">
            <label htmlFor="safe-library" className={FIELD_LABEL_CLASSES}>Target library</label>
            <select
              id="safe-library"
              name="libraryId"
              value={libraryId ?? ""}
              onChange={handleLibraryChange}
              disabled={isPending}
              className={`${FIELD_INPUT_CLASSES} w-fit`}
            >
              {libraries.map((lib) => (
                <option key={lib.id} value={lib.id}>
                  {lib.name}
                </option>
              ))}
            </select>
          </div>

          <BedPicker
            selected={bed}
            onSelect={setBed}
            onClear={() => setBed(null)}
            disabled={isPending}
          />

          <Button type="submit" disabled={isPending} className="self-start">
            {isPending ? "Generating…" : "Generate"}
          </Button>
        </form>
      </section>

      <section aria-label="Safe segments">
        <h2 className="font-display text-[1.1rem] text-ink">Segments</h2>

        {outOfScope && (
          <p role="status" className="mt-2 text-[0.82rem] text-mute">
            <span aria-label="out of rotation" className="font-semibold text-accent-2">
              Out of rotation
            </span>{" "}
            — this library is not in the station&apos;s rotation scope; these rows are parked, not
            currently playing.
          </p>
        )}

        {segments.length === 0 ? (
          <EmptyState
            className="mt-4"
            title="Nothing in the safe library yet"
            reason="Generate the first announcement using the form above."
            cta={{ label: "Start writing", onClick: focusGenerateForm }}
          />
        ) : (
          // AC2 (SPEC F28.13): scrolls sideways inside this container at
          // 390px — the page body itself never does.
          <div className="mt-4 overflow-x-auto">
            <table className="w-full border-collapse text-[0.85rem]">
              <thead>
                <tr className="border-b-2 border-line text-left">
                  <th scope="col" className={HEADER_CELL}>Title</th>
                  <th scope="col" className={HEADER_CELL}>State</th>
                  <th scope="col" className={HEADER_CELL}>Eligible</th>
                </tr>
              </thead>
              <tbody>
                {segments.map((segment) => (
                  <tr key={segment.mediaId} className="border-b border-line last:border-b-0">
                    <td className="py-2 pr-3 text-ink">{segment.title}</td>
                    <td className="py-2 pr-3 text-mute">{segment.state}</td>
                    <td className="py-2 pr-3">
                      <label className="inline-flex min-h-10 items-center gap-1.5">
                        <input
                          type="checkbox"
                          checked={segment.eligible}
                          aria-label={`Eligible: ${segment.title ?? segment.mediaId}`}
                          onChange={() => { void handleToggleEligible(segment); }}
                        />
                        <span aria-label={segment.eligible ? "eligible" : "ineligible"} className="text-mute">
                          {segment.eligible ? "Yes" : "No"}
                        </span>
                      </label>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
