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
import { FontDetailPanel } from "./FontDetailPanel";
import { FontInstallModal, type FontInstallResult } from "./FontInstallModal";
import { formatFontByteTotal } from "./font-format";
import { prettifySlug } from "./format-slug";
import { ThemeDetailPreview } from "./ThemeDetailPreview";
import { ThemeInstallModal, type ThemeInstallResult } from "./ThemeInstallModal";
import type {
  CatalogEntryDetailDto,
  CatalogEntryKind,
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
 * `ThemeShelfCard` — swatch chips painted straight off the already-fetched index row's `preview`
 * while browsing, no click-through of its own yet. Opening a theme card (PLAN T186, SPEC
 * F103.5/F103.6) reuses the SAME `GET /api/catalog/entries/{slug}` detail fetch personas already
 * use — the entry's raw manifest text rides the existing `card` wire field (SPEC F103.2's
 * generalised `{manifest, meta}` model, still named `card` on the wire, see `CatalogEntryResponse`'s
 * own remarks) — then routes to `ThemeDetailPreview` (a live composed mini-preview) instead of
 * `DetailPanel`, and "Install" opens `ThemeInstallModal` instead of `PersonaCardReviewModal`.
 *
 * A third kind, `font` (SPEC F104.1/F104.3, PLAN T201), renders `FontShelfCard` — a family-derived
 * title and a human-readable byte total painted straight off the entry's already-fetched index row's
 * own `fontFamily`/`fontByteTotal` (T194), no manifest or asset fetch on browse. Opening a font card
 * (PLAN T202, SPEC F104.4) reuses the SAME `GET /api/catalog/entries/{slug}` detail fetch every
 * other kind already uses — this time carrying T194's font-kind projection
 * (`fontFamily`/`fontSpecimenFile`/`description`) — and routes to `FontDetailPanel`, whose own
 * `SpecimenBlock` renders the pack's real hash-verified face through the asset proxy. "Install"
 * opens `FontInstallModal` instead of `PersonaCardReviewModal`/`ThemeInstallModal` — a scope
 * addition this task states plainly (see `FontDetailPanel`'s own remarks): the PLAN carries no
 * dedicated install-button task for M1, and T204's exit-check checklist has no other UI surface to
 * install a pack from.
 */
export function PersonaCatalogClient({ initialIndex }: PersonaCatalogClientProps): ReactNode {
  const router = useRouter();
  const [detail, setDetail] = useState<DetailState>({ kind: "idle" });
  const [reviewing, setReviewing] = useState(false);
  const [installingTheme, setInstallingTheme] = useState(false);
  const [installingFont, setInstallingFont] = useState(false);

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

  /** SPEC F103.6's success path: no `/themes` list page exists to land on (unlike Personas' own
   * `router.push` above) — `Station:Theme`'s choice list widening is a server-side fact the next
   * `GET /api/settings` read already reflects (PLAN T183/T184), nothing this component needs to
   * fetch or thread. Closing the modal and toasting is the whole client-side job. */
  function handleThemeInstalled(result: ThemeInstallResult): void {
    setInstallingTheme(false);
    toast.success(`"${result.name}" installed.`);
  }

  /** SPEC F104.5's success path — mirrors `handleThemeInstalled`'s own remarks: no dedicated
   * library-list page exists on THIS task's own owned files for the panel to route to (PLAN T203
   * builds that separately); closing the modal and toasting the family that just entered the
   * station's library is the whole client-side job. */
  function handleFontInstalled(result: FontInstallResult): void {
    setInstallingFont(false);
    toast.success(`"${result.family}" installed.`);
  }

  const selectedSlug = detail.kind !== "idle" ? detail.slug : null;
  const selectedEntry = entries.find((entry) => entry.slug === selectedSlug) ?? null;

  /** Routes one shelf entry to its kind's own card (review finding, T185): an exhaustive `switch`
   * over `kind`, not a two-way ternary — the SERVER already drops any kind it doesn't recognise
   * (CatalogIndexValidator, F103.1/AC6), but a ternary's `else` branch would silently render an
   * unrecognised future kind AS a persona card. The `default` here renders nothing instead, so the
   * client never lies about an entry it can't actually route, should that server invariant ever slip. */
  function renderShelfEntry(entry: CatalogShelfEntryDto): ReactNode {
    switch (entry.kind) {
      case "theme":
        return (
          <ThemeShelfCard
            key={entry.slug}
            entry={entry}
            selected={entry.slug === selectedSlug}
            onSelect={() => handleCardClick(entry.slug)}
          />
        );
      case "persona":
        return (
          <ShelfCard
            key={entry.slug}
            entry={entry}
            selected={entry.slug === selectedSlug}
            onSelect={() => handleCardClick(entry.slug)}
          />
        );
      case "font":
        return (
          <FontShelfCard
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

  /** Routes the loaded detail's own body to its entry's kind (review finding N6): the same
   * exhaustive `switch` discipline `renderShelfEntry` uses above, not a two-way `===`/`!==` check
   * against `"theme"` — the `default` renders nothing, so an entry whose kind this client doesn't
   * recognise never falls through to the persona panel by default. Widened at PLAN T202 with the
   * `"font"` arm (`FontDetailPanel`) — before this, a selected font entry fell all the way to
   * `default`, rendering nothing at all. */
  function renderDetailPanel(entry: CatalogShelfEntryDto, loaded: Extract<DetailState, { kind: "loaded" }>): ReactNode {
    // `loaded.detail.card` is `string | null`, `null` exactly when `unreachable` is `true`
    // (types.ts) — and `unreachable: true` never reaches `detail.kind === "loaded"` at all
    // (loadDetail routes it to the "error" branch instead), so this guard is a type-level
    // formality, not a real runtime path (mirrors the review-modal guard below).
    if (loaded.detail.card === null) return null;

    switch (entry.kind) {
      case "theme":
        return (
          <ThemeDetailPanel
            slug={loaded.slug}
            manifestText={loaded.detail.card}
            onInstallClick={() => setInstallingTheme(true)}
          />
        );
      case "persona":
        return <DetailPanel slug={loaded.slug} detail={loaded.detail} onImportClick={() => setReviewing(true)} />;
      case "font":
        return (
          <FontDetailPanel slug={loaded.slug} detail={loaded.detail} onInstallClick={() => setInstallingFont(true)} />
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
        <section
          aria-label={detailSectionAriaLabel(selectedEntry?.kind)}
          className="mt-6 rounded-[6px] border border-line bg-surface p-5"
        >
          {detail.kind === "loading" && (
            <div className="space-y-2">
              <Skeleton className="h-6 w-48" />
              <Skeleton className="h-4 w-full" />
              <Skeleton className="h-4 w-5/6" />
            </div>
          )}
          {detail.kind === "error" && <p className="text-[0.85rem] text-danger">{detail.message}</p>}
          {detail.kind === "loaded" && selectedEntry && renderDetailPanel(selectedEntry, detail)}
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

      {/* Cancel = no-op (SPEC F103.6's own sad path): closing this modal by any path just resets
          `installingTheme`, never touching the network — see ThemeInstallModal's own remarks. */}
      {installingTheme && detail.kind === "loaded" && selectedEntry?.kind === "theme" && detail.detail.card !== null && (
        <ThemeInstallModal
          slug={detail.slug}
          manifestText={detail.detail.card}
          onCancel={() => setInstallingTheme(false)}
          onInstalled={handleThemeInstalled}
        />
      )}

      {/* Cancel = no-op (SPEC F104.5's own sad path, mirrors the theme block above): closing this
          modal by any path just resets `installingFont`, never touching the network — see
          FontInstallModal's own remarks. No `detail.detail.card !== null` guard needed here (unlike
          the theme block above): FontInstallModal posts no body of its own, so it has nothing to
          read off `detail.detail.card` at all — only `selectedEntry?.kind === "font"` gates it. */}
      {installingFont && detail.kind === "loaded" && selectedEntry?.kind === "font" && (
        <FontInstallModal slug={detail.slug} onCancel={() => setInstallingFont(false)} onInstalled={handleFontInstalled} />
      )}
    </div>
  );
}

/** The detail panel's own aria-label, routed by the SELECTED entry's kind (T202, carrying forward
 * T201 review finding N6): the original two-way ternary against `"theme"` alone meant a selected
 * FONT entry fell through and announced "Persona details" to assistive tech — wrong. Mirrors
 * `renderDetailPanel`'s own exhaustive-switch discipline one function up; `undefined` (no entry
 * resolved yet, or an entry this client's own switch doesn't recognise) falls back to the
 * pre-existing persona label, same as `"persona"` itself. */
function detailSectionAriaLabel(kind: CatalogEntryKind | undefined): string {
  switch (kind) {
    case "theme":
      return "Theme details";
    case "font":
      return "Font pack details";
    case "persona":
    default:
      return "Persona details";
  }
}

function ThemeDetailPanel({
  slug,
  manifestText,
  onInstallClick,
}: {
  slug: string;
  manifestText: string;
  onInstallClick: () => void;
}): ReactNode {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</h2>
        {/* Install (SPEC F103.6) opens ThemeInstallModal's confirm/cancel step — this click itself
            issues no request; the modal POSTs the SAME manifestText already reviewed here. */}
        <Button type="button" variant="primary" onClick={onInstallClick}>
          Install
        </Button>
      </div>

      <ThemeDetailPreview slug={slug} manifestText={manifestText} />
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
 * A theme entry's shelf card (SPEC F103.3, F103.4, F103.5, PLAN T185/T186) — name/slug, the 18+
 * badge, and swatch chips painted straight off the entry's already-fetched `preview`, exactly like
 * browsing costs nothing beyond the one index read (F103.4, unchanged by this click-through: the
 * card itself still fetches/composes nothing while rendering). Now a `<button>`, mirroring
 * `ShelfCard`: a click routes through the SAME `handleCardClick`/`loadDetail` machinery personas
 * already use — one `GET /api/catalog/entries/{slug}` fetch, the manifest text riding the existing
 * `card` field — which `PersonaCatalogClient` then routes to `ThemeDetailPanel` (a live composed
 * mini-preview, PLAN T186) instead of `DetailPanel`.
 */
function ThemeShelfCard({
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
        <ThemeSwatchChips preview={entry.preview} />
      </button>
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

/**
 * A font entry's shelf card (SPEC F104.3, PLAN T201/T202) — a title, the 18+ badge, a small brass
 * "Font pack" kind marker (the card has no swatches/art to read its kind from at a glance, unlike a
 * theme card), and a human-readable byte total — all painted straight off the entry's already-
 * fetched index row, no manifest or asset fetch, ever, while browsing. Now a `<button>` (PLAN T202,
 * mirrors `ThemeShelfCard`'s own T185→T186 precedent): a click routes through the SAME
 * `handleCardClick`/`loadDetail` machinery every other kind already uses — one
 * `GET /api/catalog/entries/{slug}` fetch, this time carrying T194's font-kind detail projection
 * (`fontFamily`/`fontSpecimenFile`/`description`) — which `PersonaCatalogClient` then routes to
 * `FontDetailPanel` (PLAN T202) instead of `DetailPanel`/`ThemeDetailPanel`.
 *
 * The title reads `entry.fontFamily ?? prettifySlug(entry.slug)` (review finding F1), NOT a separate
 * family line under a slug-derived title: on every real pack the authoring convention is slug =
 * kebab-cased family (e.g. slug "space-grotesk" ⇒ slug-derived title "Space Grotesk" ⇒ family "Space
 * Grotesk"), so a family line under the title just repeated it verbatim. The "Font pack" micro-label
 * already carries the kind, so the title alone satisfies AC1's "shows family" without the duplicate
 * text. `fontFamily` falls back to the slug-derived title for an older index, or a malformed value
 * `CatalogIndexValidator` couldn't admit (T194) — `??`, not an `!== null` guard (review finding F2,
 * same falsy-tolerant contract `ThemeSwatchChips`'s own guard above is ruled on): a wire response
 * that OMITS the field arrives as `undefined`, not `null`, and `??` degrades to the fallback either
 * way, where an `!== null`-guarded direct render would have printed the literal word "undefined".
 *
 * Renders no `description` line at all: this story's own task line reads "meta-only:
 * family/description/byte total", but the shelf wire this card actually reads from
 * (`CatalogShelfEntryDto`) carries only `fontFamily`/`fontByteTotal` — `description` rides the
 * per-entry `GET /api/catalog/entries/{slug}` detail fetch instead (T202, `FontDetailPanel`), the
 * same shelf/detail split `ShelfCard`/`DetailPanel` already draw for a persona's own
 * `author`/`description` (STORY-281 AC1 reconciliation).
 */
function FontShelfCard({
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
          <span className="font-display text-[1.05rem] text-ink">{entry.fontFamily ?? prettifySlug(entry.slug)}</span>
          {entry.audience === "mature" && <MatureBadge />}
        </div>
        <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">Font pack</p>
        {/* `!= null` (review finding F2), not `!== null`: an omitted wire field arrives as
            `undefined`, not `null`, and `formatFontByteTotal(undefined)` would render the literal
            text "undefined B" instead of degrading — see `ThemeSwatchChips`'s own falsy-guard ruling
            above for the same reasoning against `preview`. */}
        {entry.fontByteTotal != null && (
          <p className="text-[0.82rem] text-mute">{formatFontByteTotal(entry.fontByteTotal)}</p>
        )}
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
