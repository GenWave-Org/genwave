"use client";

import { useRouter } from "next/navigation";
import { useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { readErrorMessage } from "@/lib/problem-details";
import { cn } from "@/lib/utils";
import { PersonaCardReviewModal, type PersonaCardReviewImportResult } from "../_components/PersonaCardReviewModal";
import { prettifySlug } from "./format-slug";
import type { CatalogEntryDetailDto, CatalogIndexResponseDto, CatalogShelfEntryDto } from "./types";

interface PersonaCatalogClientProps {
  /** The index this page's server component already fetched (SPEC F90.2, F90.4). */
  initialIndex: CatalogIndexResponseDto;
}

type DetailState =
  | { kind: "idle" }
  | { kind: "loading"; slug: string }
  | { kind: "loaded"; slug: string; detail: CatalogEntryDetailDto }
  | { kind: "error"; slug: string; message: string };

/**
 * The Persona Catalog shelf (STORY-233, SPEC F90.4a): a grid of entries browsed from the already-
 * fetched index (slug, 18+ badge, bestFor chips — everything `GET /api/catalog/index` carries),
 * with a click-through detail panel that loads author/description/sample patter per entry via
 * `GET /api/catalog/entries/{slug}` — one fetch per click, hash-verified and cached server-side
 * (the index route deliberately never eagerly fetches every entry's meta.json just to build the
 * grid, F90.2). Import (SPEC F90.5/F90.6, STORY-235, PLAN T103) opens `PersonaCardReviewModal`
 * with the entry's already-fetched raw card text — no second fetch, no import request until the
 * operator confirms inside that modal (the trust ruling's gate lives there, not here).
 */
export function PersonaCatalogClient({ initialIndex }: PersonaCatalogClientProps): ReactNode {
  const router = useRouter();
  const [detail, setDetail] = useState<DetailState>({ kind: "idle" });
  const [reviewing, setReviewing] = useState(false);

  // Request token (T102 review, HIGH): loadDetail's fetch is not the only thing that can change
  // `detail` between when a request starts and when it resolves — the operator can also collapse
  // the panel or select a DIFFERENT entry while it's still in flight. Every call bumps this ref
  // and captures its own value; a response only ever applies its setDetail calls if the token is
  // STILL the one it started with, otherwise it's a stale response for a selection the operator
  // has already moved past, and is silently dropped. `useRef` (not state) deliberately — bumping
  // it must never itself trigger a re-render, and it must be readable synchronously from
  // `handleCardClick`'s collapse branch, which never calls `loadDetail` at all.
  const requestTokenRef = useRef(0);

  if (initialIndex.unreachable) {
    return (
      <EmptyState
        title="Catalog unreachable"
        reason="The shelf will return when the connection to the community catalog comes back."
      />
    );
  }

  const entries = initialIndex.entries ?? [];

  if (entries.length === 0) {
    return (
      <EmptyState
        title="Nothing on the shelf yet"
        reason="The shelf will be stocked soon — check back once the community catalog has entries."
      />
    );
  }

  async function loadDetail(slug: string): Promise<void> {
    const token = ++requestTokenRef.current;
    setDetail({ kind: "loading", slug });
    try {
      const resp = await fetch(`/api/catalog/entries/${encodeURIComponent(slug)}`);
      if (requestTokenRef.current !== token) return;

      if (!resp.ok) {
        const message = await readErrorMessage(resp);
        if (requestTokenRef.current !== token) return;
        setDetail({ kind: "error", slug, message });
        return;
      }

      const body = (await resp.json()) as CatalogEntryDetailDto;
      if (requestTokenRef.current !== token) return;

      if (body.unreachable) {
        setDetail({ kind: "error", slug, message: "Catalog unreachable — try again shortly." });
        return;
      }
      setDetail({ kind: "loaded", slug, detail: body });
    } catch {
      if (requestTokenRef.current !== token) return;
      setDetail({ kind: "error", slug, message: "Network error — check your connection" });
    }
  }

  function handleCardClick(slug: string): void {
    if (detail.kind !== "idle" && detail.slug === slug) {
      // Collapsing — bump the token so a still-in-flight fetch for THIS slug can never reopen the
      // panel the operator just closed once it resolves.
      requestTokenRef.current++;
      setDetail({ kind: "idle" });
      return;
    }
    void loadDetail(slug);
  }

  /** SPEC F90.5's success path: land on Personas with the imported persona visible — `router.push`
   * (not `router.refresh`, this page has nothing to refresh) triggers a fresh server render of
   * `/personas`, which reads `GET /api/personas` itself (that page is already `force-dynamic`), so
   * the just-imported row is there without this component threading any persona state across the
   * navigation. Warnings surface as toasts — the same danger styling
   * `PersonaImportPanel`'s inline warning list uses — because they'd otherwise be stranded the
   * instant this page unmounts; the shared `Toaster` lives in the authed layout, so a toast queued
   * here outlives the navigation. */
  function handleImported(result: PersonaCardReviewImportResult): void {
    setReviewing(false);
    toast.success(`"${result.name}" ${result.created ? "imported" : "updated"}.`);
    for (const warning of result.warnings) toast.error(warning);
    router.push("/personas");
  }

  const selectedSlug = detail.kind !== "idle" ? detail.slug : null;

  return (
    <div>
      <ul aria-label="Persona catalog entries" className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {entries.map((entry) => (
          <ShelfCard
            key={entry.slug}
            entry={entry}
            selected={entry.slug === selectedSlug}
            onSelect={() => handleCardClick(entry.slug)}
          />
        ))}
      </ul>

      {detail.kind !== "idle" && (
        <section aria-label="Persona details" className="mt-6 rounded-[6px] border border-line bg-surface p-5">
          {detail.kind === "loading" && (
            <div className="space-y-2">
              <Skeleton className="h-6 w-48" />
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-5/6" />
            </div>
          )}
          {detail.kind === "error" && <p className="text-[0.85rem] text-danger">{detail.message}</p>}
          {detail.kind === "loaded" && (
            <DetailPanel slug={detail.slug} detail={detail.detail} onImportClick={() => setReviewing(true)} />
          )}
        </section>
      )}

      {/* `detail.detail.card` is `string | null`, `null` exactly when `unreachable` is `true`
          (types.ts) — and `unreachable: true` never reaches `detail.kind === "loaded"` at all
          (loadDetail routes it to the "error" branch instead), so this guard is a type-level
          formality, not a real runtime path. */}
      {reviewing && detail.kind === "loaded" && detail.detail.card !== null && (
        <PersonaCardReviewModal
          cardText={detail.detail.card}
          catalogSlug={detail.slug}
          samples={detail.detail.samplePatter ?? []}
          onCancel={() => setReviewing(false)}
          onImported={handleImported}
        />
      )}
    </div>
  );
}

function ShelfCard({
  entry,
  selected,
  onSelect,
}: {
  entry: CatalogShelfEntryDto;
  selected: boolean;
  onSelect: () => void;
}): ReactNode {
  return (
    <li>
      <button
        type="button"
        onClick={onSelect}
        aria-expanded={selected}
        className={cn(
          "flex w-full flex-col items-start gap-2 rounded-[6px] border p-4 text-left transition-colors duration-[120ms] ease-out focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent",
          selected ? "border-accent bg-surface-2" : "border-line bg-surface hover:bg-surface-2"
        )}
      >
        <div className="flex w-full items-center justify-between gap-2">
          <span className="font-display text-[1.05rem] text-ink">{prettifySlug(entry.slug)}</span>
          {entry.audience === "mature" && <MatureBadge />}
        </div>
        <BestForChips items={entry.bestFor} />
      </button>
    </li>
  );
}

function DetailPanel({
  slug,
  detail,
  onImportClick,
}: {
  slug: string;
  detail: CatalogEntryDetailDto;
  onImportClick: () => void;
}): ReactNode {
  const samplePatter = detail.samplePatter ?? [];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</h2>
          {detail.audience === "mature" && <MatureBadge />}
        </div>

        {/* Opens the full-card review modal (SPEC F90.5/F90.6, STORY-235, PLAN T103) — this click
            itself issues no request; the modal reads the card text this panel already has in
            hand from the entry fetch above. */}
        <Button type="button" variant="primary" onClick={onImportClick}>
          Import
        </Button>
      </div>

      <BestForChips items={detail.bestFor ?? []} />

      {detail.author !== null && detail.author !== "" && (
        <p className="text-[0.82rem] text-mute">By {detail.author}</p>
      )}

      {/* Plain text ONLY (SPEC F90.6) — a bare `{detail.description}` JSX child, React's default
          escaping, never dangerouslySetInnerHTML. A description containing literal markdown/HTML
          renders as those exact characters, never interpreted. */}
      {detail.description !== null && detail.description !== "" && (
        <p className="text-[0.85rem] text-ink">{detail.description}</p>
      )}

      {samplePatter.length > 0 && (
        <div>
          <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">Sample patter</p>
          <ul className="mt-2 flex list-none flex-col gap-1.5 p-0">
            {samplePatter.map((line, index) => (
              // Same plain-text rule as the description above — no interpretation of the line's
              // own content, ever.
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

/** The 18+ badge (SPEC F90.4a) — ALWAYS shown on a mature entry, never behind a toggle (ruled).
 * Pill treatment (999px radius) per the Wireless state-badge convention, brass (`--accent-2`) so
 * it reads as a clear, calm label rather than an alarm. */
function MatureBadge(): ReactNode {
  return (
    <span
      aria-label="Mature content"
      className="inline-flex w-fit shrink-0 items-center rounded-[999px] border border-accent-2 px-2 py-0.5 text-[0.68rem] font-semibold uppercase tracking-[0.08em] text-accent-2"
    >
      18+
    </span>
  );
}

/** `bestFor[]` genre chips (SPEC F90.4a) — 3px-radius bordered source-tag treatment, rendered only
 * when present (an entry with none renders nothing, not an empty container). Shared between the
 * shelf grid and the detail panel so the two never drift on how a chip looks. */
function BestForChips({ items }: { items: string[] }): ReactNode {
  if (items.length === 0) return null;

  return (
    <ul aria-label="Best for" className="m-0 flex list-none flex-wrap gap-1.5 p-0">
      {items.map((tag) => (
        <li key={tag}>
          <span className="inline-flex items-center rounded-[3px] border border-line bg-surface-2 px-1.5 py-0.5 text-[0.72rem] text-mute">
            {tag}
          </span>
        </li>
      ))}
    </ul>
  );
}
