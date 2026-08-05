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
import type {
  CatalogEntryDetailDto,
  CatalogIndexResponseDto,
  CatalogShelfEntryDto,
  CatalogThemePreview,
  CatalogThemeSwatchSet,
} from "./types";

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
 * grid, F90.2). Hire (SPEC F94.4's presentation-only catalog verb over the unchanged F90.5/F90.6
 * import request, STORY-235, PLAN T103) opens `PersonaCardReviewModal` with the entry's
 * already-fetched raw card text — no second fetch, no import request until the operator confirms
 * inside that modal (the trust ruling's gate lives there, not here).
 *
 * The shelf itself now routes each entry by `kind` (SPEC F103.1, F103.3, PLAN T185): a persona
 * entry renders the `ShelfCard`/detail-panel/Hire flow above, unchanged; a theme entry renders
 * `ThemeShelfCard` — swatch chips painted straight off the already-fetched index row's `preview`,
 * with no click-through of its own (a theme's live preview/install flow is PLAN T186's job) — so a
 * theme card costs nothing beyond the one index read, ever.
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
   * here outlives the navigation. Success copy speaks hiring language (SPEC F94.4, PLAN T130):
   * "hired" for a new row, "updated" for an existing one — the same `created` split
   * `PersonaImportPanel`'s own (unchanged, Import-speaking) success copy already uses. */
  function handleImported(result: PersonaCardReviewImportResult): void {
    setReviewing(false);
    toast.success(`"${result.name}" ${result.created ? "hired" : "updated"}.`);
    for (const warning of result.warnings) toast.error(warning);
    router.push("/personas");
  }

  const selectedSlug = detail.kind !== "idle" ? detail.slug : null;

  /** Routes one shelf entry to its kind's own card (review finding, T185): an exhaustive `switch`
   * over `kind`, not a two-way ternary — the SERVER already drops any kind it doesn't recognise
   * (CatalogIndexValidator, F103.1/AC6), but a ternary's `else` branch would silently render an
   * unrecognised future kind AS a persona card. The `default` here renders nothing instead, so the
   * client never lies about an entry it can't actually route, should that server invariant ever slip. */
  function renderShelfEntry(entry: CatalogShelfEntryDto): ReactNode {
    switch (entry.kind) {
      case "theme":
        return <ThemeShelfCard key={entry.slug} entry={entry} />;
      case "persona":
        return (
          <ShelfCard
            key={entry.slug}
            entry={entry}
            selected={entry.slug === selectedSlug}
            onSelect={() => handleCardClick(entry.slug)}
          />
        );
      default:
        return null;
    }
  }

  return (
    <div>
      <ul aria-label="Community catalog entries" className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {entries.map(renderShelfEntry)}
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
          verb="hire"
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

/**
 * A theme entry's shelf card (SPEC F103.3, F103.4, PLAN T185) — name/slug, the 18+ badge, and
 * swatch chips painted straight off the entry's already-fetched `preview`, nothing else. Static
 * (a `<div>`, not a `<button>`): unlike a persona card, there is no click-through detail fetch to
 * open here yet — a theme's live manifest preview and install flow is PLAN T186's own task, and
 * wiring a click that fetched the manifest here would violate this task's own "no manifest fetch
 * while browsing the shelf" contract (F103.4).
 */
function ThemeShelfCard({ entry }: { entry: CatalogShelfEntryDto }): ReactNode {
  return (
    <li>
      <div className="flex w-full flex-col items-start gap-2 rounded-[6px] border border-line bg-surface p-4">
        <div className="flex w-full items-center justify-between gap-2">
          <span className="font-display text-[1.05rem] text-ink">{prettifySlug(entry.slug)}</span>
          {entry.audience === "mature" && <MatureBadge />}
        </div>
        <ThemeSwatchChips preview={entry.preview} />
      </div>
    </li>
  );
}

/** The five swatch tokens, in the catalog schema's own authored order (background through
 * accent) — shared between the chip row below and anything else that ever needs to walk a
 * `CatalogThemeSwatchSet` in a stable, meaningful order. */
const SWATCH_TOKEN_ORDER: ReadonlyArray<keyof CatalogThemeSwatchSet> = ["bg", "surface", "ink", "accent", "accent-2"];

/**
 * A theme card's colour-chip row (SPEC F103.4) — five small swatches painted with the theme's OWN
 * declared hex values via an inline `backgroundColor` style, a deliberate, narrow exception to this
 * codebase's "semantic tokens only" rule (design-aesthetic): these colours ARE the theme being
 * previewed, not app chrome, so there is no app token that could stand in for them. Renders only the
 * LIGHT mode's five swatches, not both modes' ten — a shelf scan wants one quick read per card, and
 * dark-mode fidelity belongs to the live, composed preview PLAN T186 adds at the detail view, not a
 * doubled chip row here. Renders nothing when the entry carries no `preview` (an older index, or any
 * shape T185's tolerant validator couldn't complete) — the card still shows its name, just no chips.
 * The row is `aria-hidden` (review finding): five empty `<li>`s carrying only a decorative,
 * un-labelled swatch announce as noise to a screen reader — the theme's own name (rendered above
 * this row by `ThemeShelfCard`) already carries every bit of the card's semantics. `data-testid`
 * (not an ARIA attribute) is how a spec locates the row instead, since it is deliberately outside
 * the accessibility tree.
 */
function ThemeSwatchChips({ preview }: { preview: CatalogThemePreview | null }): ReactNode {
  // Falsy guard (review finding), not `preview === null`: the wire type is `CatalogThemePreview |
  // null`, but a strict `=== null` check couples this component to the api's CURRENT
  // `DefaultIgnoreCondition = Never` serialization posture — if the api ever started omitting null
  // properties instead, this field would arrive as `undefined`, `=== null` would be false, and
  // indexing `preview.light` below would crash the whole shelf render. `!preview` degrades to no
  // chips either way, without caring which of the two absent-value shapes the wire actually sends.
  if (!preview) return null;

  return (
    <ul aria-hidden="true" data-testid="theme-preview-swatches" className="m-0 flex list-none gap-1 p-0">
      {SWATCH_TOKEN_ORDER.map((token) => (
        <li key={token}>
          <span
            className="block h-5 w-5 rounded-[3px] border border-line"
            style={{ backgroundColor: preview.light[token] }}
          />
        </li>
      ))}
    </ul>
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

        {/* "Hire" (SPEC F94.4's catalog verb pass, gh-#169, PLAN T130) opens the full-card review
            modal (SPEC F90.5/F90.6, STORY-235, PLAN T103) — this click itself issues no request;
            the modal reads the card text this panel already has in hand from the entry fetch
            above, and its own confirm button speaks the same verb via `verb="hire"` below. */}
        <Button type="button" variant="primary" onClick={onImportClick}>
          Hire
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
