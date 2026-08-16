"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useMemo, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { describeStationDefault, type PersonaImportSuccessBody } from "@/lib/persona-import-api";
import { readErrorMessage } from "@/lib/problem-details";
import { cn } from "@/lib/utils";
import { personaSlug } from "../personas/persona-slug";
import {
  describeTasteContext,
  describeTastePredicate,
  formatWeight,
  parsePersonaCardReview,
  type PersonaCardReview,
  type PersonaCardReviewCorrection,
  type PersonaCardReviewTasteRule,
} from "./persona-card-review";
import { PersonaCardReviewFace } from "./PersonaCardReviewFace";

export interface PersonaCardReviewImportResult {
  name: string;
  created: boolean;
  warnings: string[];
}

/** The confirm button's presentation-only verb (SPEC F94.4, gh-#169, PLAN T130) — never threaded
 * into the wire request itself; see `PersonaCardReviewModalProps.verb` and `handleConfirm` below,
 * whose POST target is exactly the same `.../import` endpoint regardless of which verb is showing. */
export type PersonaCardReviewVerb = "import" | "hire";

export interface PersonaCardReviewModalProps {
  /** Raw card JSON text — exactly as fetched from the catalog or read from an uploaded file (SPEC
   * F90.5, F90.6). POSTed byte-for-byte on confirm; never the parsed projection below, which
   * would silently drop any field its narrower read doesn't know about. */
  cardText: string;
  /** Present only when this card's origin is a catalog entry (SPEC F90.7's provenance stamp) —
   * appended as `?catalogSlug=` on import. Omitted entirely for a file-upload origin
   * (STORY-236/T104), which stamps `imported_from = "file"` server-side by default. */
  catalogSlug?: string;
  /** The catalog entry's own sidecar face — `CatalogEntryDetailDto.personaAvatarFile` (SPEC F128.7,
   * PLAN T297), or `null`/omitted when the entry declares none. Meaningless without `catalogSlug`
   * (there is no asset route without an entry to resolve it against) — the file-upload origin
   * simply never passes this prop, mirroring `samples`' own "omitted there" shape exactly. */
  avatarFile?: string | null;
  /** Sample patter lines from the catalog entry's `meta.json` sidecar (SPEC F90.5's "Entry =
   * unchanged F79 card + meta.json sidecar" split) — the trust ruling's "samples when present"
   * requirement, shown alongside the card's own sections. A file-upload origin carries no sidecar
   * at all, so this is simply omitted there. */
  samples?: string[];
  /** Which verb the confirm button speaks (SPEC F94.4's catalog "Hire" pass) — presentation only,
   * the wire stays "import" either way (`handleConfirm`'s URL is untouched by this prop). The
   * catalog origin (`PersonaCatalogClient`) passes `"hire"`; the file-upload origin
   * (`PersonaImportPanel`) omits this prop entirely and keeps the default `"import"` wording,
   * unchanged (the issue's own lean: file uploads stay "Import"). */
  verb?: PersonaCardReviewVerb;
  onCancel: () => void;
  onImported: (result: PersonaCardReviewImportResult) => void;
}

type ConfirmStatus = { kind: "idle" } | { kind: "importing" } | { kind: "error"; message: string };

const SECTION_LABEL_CLASSES = "text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";
const SECTION_BODY_CLASSES = "mt-1.5 text-[0.85rem] text-ink";
const LIST_CLASSES = "m-0 mt-1.5 flex list-none flex-col gap-1.5 p-0";
const LIST_ITEM_CLASSES = "rounded-[6px] border border-line bg-surface-2 px-3 py-2 text-[0.85rem] text-ink";

/** Confirm button copy per verb (SPEC F94.4) — a `Record` over `PersonaCardReviewVerb` so adding a
 * third verb someday is a compile error here until it's given both an idle and busy label, rather
 * than a silently-blank button. */
const CONFIRM_LABEL: Record<PersonaCardReviewVerb, { idle: string; busy: string }> = {
  import: { idle: "Confirm import", busy: "Importing…" },
  hire: { idle: "Confirm hire", busy: "Hiring…" },
};

function TextSection({ label, value }: { label: string; value: string }): ReactNode {
  return (
    <div>
      <h3 className={SECTION_LABEL_CLASSES}>{label}</h3>
      <p className={SECTION_BODY_CLASSES}>{value === "" ? "—" : value}</p>
    </div>
  );
}

/**
 * One generic list-backed section (review finding #8, collapsing the former ListSection/
 * CorrectionsSection/TasteSection trio): a label, then either every item (each wrapped in the
 * same bordered row) or a plain "None" — the trust ruling's sections are always shown, never
 * hidden for being empty (contrast the caller-gated Sample-patter/Other-fields sections below,
 * which omit themselves entirely when there's nothing to show).
 */
function Section<T>({
  label,
  items,
  renderItem,
  itemClassName,
}: {
  label: string;
  items: T[];
  renderItem: (item: T, index: number) => ReactNode;
  itemClassName?: string;
}): ReactNode {
  return (
    <div>
      <h3 className={SECTION_LABEL_CLASSES}>{label}</h3>
      {items.length === 0 ? (
        <p className={SECTION_BODY_CLASSES}>None</p>
      ) : (
        <ul aria-label={label} className={LIST_CLASSES}>
          {items.map((item, index) => (
            <li key={index} className={cn(LIST_ITEM_CLASSES, itemClassName)}>
              {renderItem(item, index)}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function VoiceSection({ voice }: { voice: PersonaCardReview["voice"] }): ReactNode {
  return (
    <div>
      <h3 className={SECTION_LABEL_CLASSES}>Voice</h3>
      <p className={SECTION_BODY_CLASSES}>
        Engine: {describeStationDefault(voice.engine)} · Voice: {describeStationDefault(voice.voiceId)}
      </p>
    </div>
  );
}

/** A correction whose `from`/`to` both parsed to "" almost certainly wasn't a `{from, to}` pair to
 * begin with (SPEC `PersonaCorrection`'s two required strings) — the parser degrades a
 * non-string/missing field to "" rather than failing the whole review (F79.2 tolerance), but
 * silently rendering that as a blank " → " row reads as a rendering bug, not an honest reflection
 * of what's actually in the card (review finding #10). */
function isUnreadableCorrection(correction: PersonaCardReviewCorrection): boolean {
  return correction.from === "" && correction.to === "";
}

function renderCorrection(correction: PersonaCardReviewCorrection): ReactNode {
  if (isUnreadableCorrection(correction)) {
    return <span className="text-mute">Unreadable correction entry</span>;
  }
  return (
    <>
      <span>{correction.from}</span>
      <span aria-hidden="true"> → </span>
      <span>{correction.to}</span>
    </>
  );
}

function renderTasteRule(rule: PersonaCardReviewTasteRule): ReactNode {
  return (
    <>
      <span>{describeTastePredicate(rule.predicate)}</span>
      <span className="text-[0.78rem] text-mute">
        {describeTasteContext(rule.context)} · weight {formatWeight(rule.weight)}
      </span>
    </>
  );
}

function renderOtherField([key, value]: [string, unknown]): ReactNode {
  return (
    <>
      <span className="font-semibold">{key}</span>: {JSON.stringify(value)}
    </>
  );
}

/** Every SPEC F90.6/ARCHITECTURE.md "Trust ruling" section, plain text (React's default escaping
 * only — no `dangerouslySetInnerHTML` anywhere in this tree, ever). `catalogSlug`/`avatarFile` (SPEC
 * F128.7, PLAN T297) render the entry's own face beside its Name — `catalogSlug === undefined` (the
 * file-upload origin) never even mounts `PersonaCardReviewFace`, since there is no asset route to
 * build without an entry slug; `PersonaCardReviewFace` itself already renders nothing for a
 * catalog-origin entry that simply declares no face (`avatarFile === null`). */
function ReviewBody({
  review,
  samples,
  catalogSlug,
  avatarFile,
}: {
  review: PersonaCardReview;
  samples: string[];
  catalogSlug: string | undefined;
  avatarFile: string | null | undefined;
}): ReactNode {
  const otherFieldEntries = Object.entries(review.otherFields);

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        {catalogSlug !== undefined && (
          <PersonaCardReviewFace slug={catalogSlug} file={avatarFile ?? null} personaName={review.name} />
        )}
        <div>
          <h3 className={SECTION_LABEL_CLASSES}>Name</h3>
          <p className={SECTION_BODY_CLASSES}>{review.name}</p>
        </div>
      </div>
      <TextSection label="Tagline" value={review.tagline} />
      <TextSection label="Soul" value={review.soul} />
      <Section label="Quirks" items={review.quirks} renderItem={(item) => item} />
      <VoiceSection voice={review.voice} />
      <div>
        <h3 className={SECTION_LABEL_CLASSES}>Energy disposition</h3>
        <p className={SECTION_BODY_CLASSES}>{review.energyDisposition}</p>
      </div>
      <Section label="Corrections" items={review.corrections} renderItem={renderCorrection} />
      <Section label="Lore" items={review.lore} renderItem={(item) => item} />
      <Section
        label="Taste"
        items={review.taste}
        renderItem={renderTasteRule}
        itemClassName="flex flex-col gap-0.5"
      />
      {samples.length > 0 && <Section label="Sample patter" items={samples} renderItem={(item) => item} />}
      {/* Review finding #6 — the "reviewed vs posted" gate: any top-level card key this review's
          named sections above don't already show still gets a row here, so confirm can never POST
          a byte the operator never saw named. Omitted entirely (not "None") when the card carries
          nothing beyond what's already shown, mirroring Sample patter's own presence-gating. */}
      {otherFieldEntries.length > 0 && (
        <Section label="Other fields in this card" items={otherFieldEntries} renderItem={renderOtherField} />
      )}
    </div>
  );
}

/**
 * The trust ruling's required stop (ARCHITECTURE.md "Trust ruling", amended 2026-07-26; SPEC
 * F90.6, STORY-235 AC1): renders a card's FULL text and issues NO import request of any kind
 * until the operator explicitly clicks Confirm — opening, scrolling, or cancelling never touches
 * `fetch`. Deliberately generic over its origin (`cardText` + the optional `catalogSlug`/`samples`
 * "source descriptor") so this same component is the one T104 (STORY-236) reuses for the
 * file-upload door — the only door this ruling recognizes is "reviewed", never "which button
 * started it". `verb` (SPEC F94.4, PLAN T130) is the one PRESENTATION-only fork this shared
 * component carries: the confirm button's label, never `handleConfirm`'s request.
 *
 * House modal conventions: Radix `Dialog` for the focus trap and Escape/backdrop dismissal
 * (`confirm-dialog.tsx`, `MobileNav.tsx` — FocusScope + DismissableLayer are Radix's job, never
 * hand-rolled here); the body scrolls independently of the fixed header/footer so a long card
 * never pushes the Confirm button off a short viewport (390px mobile included). Cancel, Escape,
 * and a backdrop click all route through the same `onOpenChange` → `onCancel` path, so there is
 * exactly one "nothing happened" exit, not three slightly different ones.
 *
 * Focus restoration (review finding #1): unlike `MobileNav.tsx`'s real rendered
 * `<Dialog.Trigger>` (where Radix's own built-in trigger-refocus already suffices), this
 * component has no trigger Radix could refocus automatically — same as `confirm-dialog.tsx`'s
 * own single persistent `<Dialog.Root>`, which ALSO renders no `Dialog.Trigger` and hand-wires
 * its own `restoreFocusRef` for exactly this reason. This component follows that same pattern:
 * it mounts fresh on every open (a new component instance each time the parent shows it), so
 * `restoreFocusRef` captures `document.activeElement` inline during that first render — before
 * Radix's own `FocusScope` mount effect ever runs — the same "capture before the dialog steals
 * focus" moment `confirm-dialog.tsx`'s `confirm()` captures explicitly at call time;
 * `onCloseAutoFocus` below then prevents Radix's default (moving focus into the by-then-unmounted
 * content) and restores it by hand, so closing this modal by ANY path (Cancel, Escape, backdrop,
 * or a successful import) hands focus back to whatever the operator was on before — normally the
 * Import button that opened it.
 */
export function PersonaCardReviewModal({
  cardText,
  catalogSlug,
  avatarFile,
  samples = [],
  verb = "import",
  onCancel,
  onImported,
}: PersonaCardReviewModalProps): ReactNode {
  const [status, setStatus] = useState<ConfirmStatus>({ kind: "idle" });
  const review = useMemo(() => parsePersonaCardReview(cardText), [cardText]);

  // Lazy-captured exactly once per mount (guarded by the `undefined` sentinel, not re-read on
  // every re-render — a later render's `document.activeElement` would already be inside this
  // dialog, which is worthless as a restore target).
  const restoreFocusRef = useRef<HTMLElement | null | undefined>(undefined);
  if (restoreFocusRef.current === undefined) {
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  }

  async function handleConfirm(): Promise<void> {
    if (review === null || status.kind === "importing") return;
    setStatus({ kind: "importing" });

    const slug = personaSlug(review.name);
    const url =
      catalogSlug === undefined
        ? `/api/personas/${slug}/import`
        : `/api/personas/${slug}/import?catalogSlug=${encodeURIComponent(catalogSlug)}`;

    try {
      const resp = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: cardText,
      });

      if (resp.status === 201 || resp.status === 200) {
        const body = (await resp.json()) as PersonaImportSuccessBody;
        onImported({ name: body.name, created: resp.status === 201, warnings: body.warnings });
        return;
      }

      setStatus({ kind: "error", message: await readErrorMessage(resp) });
    } catch {
      setStatus({ kind: "error", message: "Network error — check your connection" });
    }
  }

  return (
    <Dialog.Root
      open
      onOpenChange={(open) => {
        if (!open) onCancel();
      }}
    >
      <Dialog.Portal>
        <Dialog.Overlay
          data-testid="persona-card-review-overlay"
          className="fixed inset-0 z-50 bg-ink/40 transition-opacity duration-200 ease-out motion-reduce:transition-none"
        />
        <Dialog.Content
          aria-label="Review persona card"
          className="fixed left-1/2 top-1/2 z-50 flex max-h-[85vh] w-[calc(100%-2rem)] max-w-xl -translate-x-1/2 -translate-y-1/2 flex-col rounded-[6px] border border-line bg-surface p-6 transition-opacity duration-200 ease-out focus:outline-none motion-reduce:transition-none"
          onCloseAutoFocus={(event) => {
            event.preventDefault();
            restoreFocusRef.current?.focus();
          }}
        >
          <Dialog.Title className="font-display text-[1.1rem] text-ink">
            {review === null ? "Review card" : `Review "${review.name}"`}
          </Dialog.Title>
          <Dialog.Description className="mt-1 text-[0.82rem] text-mute">
            The full card, exactly as authored. Nothing is imported until you confirm.
          </Dialog.Description>

          <div className="mt-4 min-h-0 flex-1 overflow-y-auto pr-1">
            {review === null ? (
              <p role="alert" className="text-[0.85rem] text-danger">
                This card couldn&apos;t be read — it may be malformed. Cancel and try again.
              </p>
            ) : (
              <ReviewBody review={review} samples={samples} catalogSlug={catalogSlug} avatarFile={avatarFile} />
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
              disabled={review === null || status.kind === "importing"}
            >
              {status.kind === "importing" ? CONFIRM_LABEL[verb].busy : CONFIRM_LABEL[verb].idle}
            </Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
