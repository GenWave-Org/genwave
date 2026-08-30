"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { Skeleton } from "@/components/ui/skeleton";
import { readErrorMessage } from "@/lib/problem-details";
import { useRestoreFocus } from "@/lib/use-restore-focus";
import { BestForChips, MatureBadge } from "./catalog-badges";
import { parseShowCardReview, rotationRuleLine, type ShowCardReview } from "./show-card-review";
import type { CatalogEntryDetailDto } from "./types";

export interface ShowCardReviewImportResult {
  slug: string;
  name: string;
  tagline: string | null;
  flavor: string | null;
  /** Carried straight through from the entry fetch this modal already made (SPEC F118.3) — the
   * import response itself (`ShowDto`) has no such field, `ShowsController.Import` never reads or
   * acts on it (see that action's own remarks); `PersonaCatalogClient` reads this to decide the
   * soft "also hire" offer, never re-fetching the entry a second time to get it. */
  suggestedPersona: string | null;
}

export interface ShowCardReviewModalProps {
  /** The catalog entry's own slug — used as BOTH the import route's target slug and the
   * `?catalogSlug=` provenance value (SPEC F90.7's persona precedent, applied to the show kind by
   * PLAN T254/T255: a catalog show imports under the same slug it is known by on the shelf). */
  slug: string;
  /** Whether a show already exists under this slug locally (PLAN T255, the font/theme
   * installed-state precedent — gh-#375's "reopening shows no installed state" lesson, applied here
   * so a re-import is never a silent surprise). Drives the Confirm button's label and an "Imported"
   * chip; the ACTUAL authored-vs-imported collision gate is still enforced server-side
   * (`ShowsController.Import`'s own SPEC F115.5 rule) — this prop only makes the UI honest about
   * what a confirm is likely to do, it never blocks the click itself. */
  alreadyImported: boolean;
  /** Cancel, Escape, or a backdrop click — zero requests, mirrors every other house modal. */
  onCancel: () => void;
  onImported: (result: ShowCardReviewImportResult) => void;
}

type EntryState =
  | { kind: "loading" }
  | { kind: "loaded"; detail: CatalogEntryDetailDto; card: string }
  | { kind: "error"; message: string };

type ConfirmStatus = { kind: "idle" } | { kind: "importing" } | { kind: "error"; message: string };

/** Wire shape of a successful `POST /api/shows/{slug}/import` (mirrors `GenWave.Host.Api.ShowDto`
 * field for field, narrowed to the fields this modal actually reads). */
interface ShowImportSuccessBody {
  slug: string;
  name: string;
  tagline: string | null;
  flavor: string | null;
}

const SECTION_LABEL_CLASSES = "text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";
const SECTION_BODY_CLASSES = "mt-1.5 text-[0.85rem] text-ink";

function TextSection({ label, value }: { label: string; value: string }): ReactNode {
  return (
    <div>
      <h3 className={SECTION_LABEL_CLASSES}>{label}</h3>
      <p className={SECTION_BODY_CLASSES}>{value === "" ? "—" : value}</p>
    </div>
  );
}

/**
 * The show manifest's FULL text (SPEC F118.2, PLAN T255) — name, tagline, and a visually explicit
 * "Flavor (feeds the DJ's prompt)" section: this modal IS the ruled primary gate for imported
 * flavor (the F90 trust posture; the T249/T254 constraint chain named this modal as its own
 * endpoint), so flavor is never a bare label indistinguishable from tagline. `bestFor`/`author`/
 * `description`/`samplePatter` are the entry's meta.json context (SPEC F90.4a) — the same
 * supplementary fields `DetailPanel`/`FontDetailPanel` show at browse time for every other kind,
 * folded into this one combined fetch+review+confirm modal rather than a separate always-visible
 * panel (PLAN T255's own dispatch note: a show manifest is three fields — there is no meaningful
 * "browse, then decide to review" step to split out the way a persona card's dozen sections earn
 * one). A schema 1.1 `envelope.rotation` (SPEC F152.6, PLAN T363) renders one plain-text line right
 * beside flavor — "the rule in words" (e.g. "Plays tracks aired 0 times") — when the manifest
 * carries one; a 1.0 manifest, or one whose `envelope` carries no rotation, renders nothing extra
 * here at all.
 */
function ReviewBody({
  review,
  detail,
}: {
  review: ShowCardReview;
  detail: CatalogEntryDetailDto;
}): ReactNode {
  const bestFor = detail.bestFor ?? [];
  const samplePatter = detail.samplePatter ?? [];

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h3 className={SECTION_LABEL_CLASSES}>Name</h3>
        <p className={SECTION_BODY_CLASSES}>{review.name}</p>
      </div>
      <TextSection label="Tagline" value={review.tagline} />
      <TextSection label="Flavor (feeds the DJ's prompt)" value={review.flavor} />
      {review.rotation !== null && (
        <p className={SECTION_BODY_CLASSES}>{rotationRuleLine(review.rotation)}</p>
      )}

      <BestForChips items={bestFor} />

      {detail.author !== null && detail.author !== "" && (
        <p className="text-[0.82rem] text-mute">By {detail.author}</p>
      )}

      {/* Plain text ONLY (SPEC F90.6's rule, mirrored) — a bare JSX child, React's default
          escaping, never dangerouslySetInnerHTML. */}
      {detail.description !== null && detail.description !== "" && (
        <p className="text-[0.85rem] text-ink">{detail.description}</p>
      )}

      {samplePatter.length > 0 && (
        <div>
          <p className={SECTION_LABEL_CLASSES}>Sample patter</p>
          <ul className="mt-2 flex list-none flex-col gap-1.5 p-0">
            {samplePatter.map((line, index) => (
              <li
                key={`${line}-${index}`}
                className="rounded-[6px] border border-line bg-surface-2 px-3 py-2 text-[0.85rem] text-ink"
              >
                {line}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

/**
 * The show catalog's combined detail-and-review modal (SPEC F118.1, F118.2; STORY-315; PLAN T255):
 * clicking a show shelf card opens THIS modal directly — it owns its own `GET
 * /api/catalog/entries/{slug}` fetch (unlike `PersonaCardReviewModal`/`ThemeInstallModal`, which
 * receive an already-fetched card from `PersonaCatalogClient`'s shared `detail` state) — because a
 * show's browse step and its trust-ruling review step are the SAME stop: there is no meaningfully
 * smaller "just the name and tagline" preview to show first the way a theme's live swatches or a
 * font's specimen face earn one. Opening, loading, or cancelling issues no import request of any
 * kind; only Confirm does (the same required stop `PersonaCardReviewModal` states for its own kind).
 *
 * House modal conventions mirrored from `PersonaCardReviewModal`/`ThemeInstallModal`: Radix
 * `Dialog` for the focus trap and Escape/backdrop dismissal; hand-wired focus restoration (this
 * component mounts fresh with no real `Dialog.Trigger` of its own); Cancel, Escape, and a backdrop
 * click all route through the same `onOpenChange` → `onCancel` path.
 */
export function ShowCardReviewModal({
  slug,
  alreadyImported,
  onCancel,
  onImported,
}: ShowCardReviewModalProps): ReactNode {
  const [entryState, setEntryState] = useState<EntryState>({ kind: "loading" });
  const [status, setStatus] = useState<ConfirmStatus>({ kind: "idle" });

  useEffect(() => {
    let cancelled = false;
    setEntryState({ kind: "loading" });

    (async () => {
      try {
        const resp = await fetch(`/api/catalog/entries/${encodeURIComponent(slug)}`);
        if (cancelled) return;

        if (!resp.ok) {
          const message = await readErrorMessage(resp);
          if (cancelled) return;
          setEntryState({ kind: "error", message });
          return;
        }

        const body = (await resp.json()) as CatalogEntryDetailDto;
        if (cancelled) return;

        if (body.unreachable || body.card === null) {
          setEntryState({ kind: "error", message: "Catalog unreachable — try again shortly." });
          return;
        }
        setEntryState({ kind: "loaded", detail: body, card: body.card });
      } catch {
        if (cancelled) return;
        setEntryState({ kind: "error", message: "Network error — check your connection" });
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [slug]);

  const review = useMemo(
    () => (entryState.kind === "loaded" ? parseShowCardReview(entryState.card) : null),
    [entryState]
  );

  const restoreFocus = useRestoreFocus("on-mount");

  async function handleConfirm(): Promise<void> {
    if (entryState.kind !== "loaded" || review === null || status.kind === "importing") return;
    setStatus({ kind: "importing" });

    const encodedSlug = encodeURIComponent(slug);

    try {
      const resp = await fetch(`/api/shows/${encodedSlug}/import?catalogSlug=${encodedSlug}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: entryState.card,
      });

      if (resp.ok) {
        const body = (await resp.json()) as ShowImportSuccessBody;
        onImported({
          slug: body.slug,
          name: body.name,
          tagline: body.tagline,
          flavor: body.flavor,
          suggestedPersona: entryState.detail.suggestedPersona,
        });
        return;
      }

      setStatus({ kind: "error", message: await readErrorMessage(resp) });
    } catch {
      setStatus({ kind: "error", message: "Network error — check your connection" });
    }
  }

  const audience = entryState.kind === "loaded" ? entryState.detail.audience : null;
  const confirmDisabled = entryState.kind !== "loaded" || review === null || status.kind === "importing";
  const confirmLabel =
    status.kind === "importing" ? "Importing…" : alreadyImported ? "Confirm re-import" : "Confirm import";

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onCancel();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay
          data-testid="show-card-review-overlay"
          className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none"
        />
        <Dialog.Content
          aria-label="Review show"
          className="fixed left-1/2 top-1/2 z-50 flex max-h-[85vh] w-[calc(100%-2rem)] max-w-xl -translate-x-1/2 -translate-y-1/2 flex-col rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={restoreFocus.onCloseAutoFocus}
        >
          <div className="flex flex-wrap items-center gap-2">
            <Dialog.Title className="font-display text-[1.1rem] text-ink">
              {review === null ? "Review show" : `Review "${review.name}"`}
            </Dialog.Title>
            {alreadyImported && <Chip>Imported</Chip>}
            {audience === "mature" && <MatureBadge />}
          </div>
          <Dialog.Description className="mt-1 text-[0.82rem] text-mute">
            The full show, exactly as authored. Nothing is imported until you confirm.
          </Dialog.Description>

          <div className="mt-4 min-h-0 flex-1 overflow-y-auto pr-1">
            {entryState.kind === "loading" && (
              <div className="space-y-2">
                <Skeleton className="h-6 w-48" />
                <Skeleton className="h-4 w-full" />
                <Skeleton className="h-4 w-5/6" />
              </div>
            )}
            {entryState.kind === "error" && (
              <p role="alert" className="text-[0.85rem] text-danger">
                {entryState.message}
              </p>
            )}
            {entryState.kind === "loaded" && review === null && (
              <p role="alert" className="text-[0.85rem] text-danger">
                This show couldn&apos;t be read — it may be malformed. Cancel and try again.
              </p>
            )}
            {entryState.kind === "loaded" && review !== null && (
              <ReviewBody review={review} detail={entryState.detail} />
            )}
          </div>

          {status.kind === "error" && (
            <p role="alert" className="mt-3 text-[0.85rem] text-danger">
              {status.message}
            </p>
          )}

          <div className="mt-5 flex justify-end gap-2">
            <Button type="button" variant="secondary" onClick={onCancel} disabled={status.kind === "importing"}>
              Cancel
            </Button>
            <Button
              type="button"
              onClick={() => {
                void handleConfirm();
              }}
              disabled={confirmDisabled}
            >
              {confirmLabel}
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
