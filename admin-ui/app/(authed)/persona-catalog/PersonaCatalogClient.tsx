"use client";

import * as Dialog from "@radix-ui/react-dialog";
import { useRouter } from "next/navigation";
import { useMemo, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Chip } from "@/components/ui/chip";
import { DialogShell } from "@/components/ui/dialog-shell";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { toast } from "@/components/ui/toast";
import { clampPackDisplayText } from "@/lib/clamp-pack-display-text";
import { formatDateStamp } from "@/lib/format-clock";
import { readErrorMessage } from "@/lib/problem-details";
import { useRestoreFocus } from "@/lib/use-restore-focus";
import { cn } from "@/lib/utils";
import { PersonaCardReviewModal, type PersonaCardReviewImportResult } from "../_components/PersonaCardReviewModal";
import { AdPackDetailPanel } from "./AdPackDetailPanel";
import { AdPackInstallModal, type AdPackInstallResult } from "./AdPackInstallModal";
import { AvatarDetailPanel } from "./AvatarDetailPanel";
import { AvatarInstallModal, type AvatarInstallResult } from "./AvatarInstallModal";
import { BestForChips, MatureBadge } from "./catalog-badges";
import { FontDetailPanel } from "./FontDetailPanel";
import { FontInstallModal, type FontInstallResult } from "./FontInstallModal";
import { formatFontByteTotal } from "./font-format";
import { prettifySlug } from "./format-slug";
import { IconDetailPanel } from "./IconDetailPanel";
import { IconInstallModal, type IconInstallResult } from "./IconInstallModal";
import { ShowCardReviewModal, type ShowCardReviewImportResult } from "./ShowCardReviewModal";
import { ThemeDetailPreview } from "./ThemeDetailPreview";
import { ThemeInstallModal, type ThemeInstallResult } from "./ThemeInstallModal";
import type {
  CatalogEntryDetailDto,
  CatalogEntryKind,
  CatalogIndexResponseDto,
  CatalogShelfEntryDto,
  CatalogThemePreview,
  CatalogThemeSwatchSet,
  ThemeCatalogProvenanceDto,
} from "./types";

/** Plural noun per kind for the per-tab empty state (gh-#372) — matches each kind's own shelf
 * vocabulary ("font packs", the F104 wording, not bare "fonts"). `Record<CatalogEntryKind, string>`
 * requires every member, the same exhaustiveness discipline `renderShelfEntry`'s own switch states
 * explicitly. */
const KIND_TAB_NOUN: Record<CatalogEntryKind, string> = {
  persona: "personas",
  theme: "themes",
  font: "font packs",
  show: "shows",
  avatar: "avatar packs",
  icon: "icons",
  "ad-pack": "ad packs",
};

interface PersonaCatalogClientProps {
  /** The index this page's server component already fetched (SPEC F90.2, F90.4). */
  initialIndex: CatalogIndexResponseDto;
  /**
   * Slugs already installed, per `GET /api/fonts` (PLAN T204, Dean's post-v3.1.0 review: reopening
   * an installed pack's detail panel showed no sign it was already installed). The page's own server
   * component fetches this ALONGSIDE the index (the smaller diff over a lazy per-open fetch — one
   * extra `Promise.all` leg server-side, versus threading a second client-side fetch/loading state
   * through `loadDetail` for font entries only) and hands the slug list straight through; defaults to
   * `[]` — fail closed, matching this file's own `catalogEnabled` default posture elsewhere in the
   * app — so an isolated render with no live signal never CLAIMS a pack is installed that it has no
   * evidence for.
   */
  installedFontSlugs?: string[];
  /**
   * Every catalog-imported theme's provenance, per `GET /api/settings`'s own `Station:Theme` choices
   * (gh-#375 — the theme half of the same reopening-shows-no-installed-state complaint the
   * font half above already closed). Mirrors `installedFontSlugs`'s own shape and defaults ([]) —
   * fail closed, so an isolated render with no live signal never CLAIMS a theme is installed that it
   * has no evidence for — see `ThemeCatalogProvenanceDto`'s own remarks for why this rides
   * `/api/settings` rather than a new backend route.
   */
  installedThemeProvenance?: ThemeCatalogProvenanceDto[];
  /**
   * Every catalog slug already imported as a local show, per `GET /api/shows` (SPEC F118.1, PLAN
   * T255) — mirrors `installedFontSlugs`'s own "reopening shows no installed state" fix (gh-#375),
   * applied to the show kind: an `ImportedFrom`-bearing OR authored-under-the-same-slug row both
   * count, since either way a re-import off this exact slug is a real, already-informed choice, not
   * a surprise. Drives `ShowShelfCard`'s "Imported" chip and `ShowCardReviewModal`'s
   * Confirm-relabel; the actual authored-vs-imported collision gate stays server-side (SPEC F115.5),
   * this prop only keeps the UI honest. Defaults to `[]` — fail closed, same posture as
   * `installedFontSlugs`/`installedThemeProvenance` above: no live signal never CLAIMS a slug is
   * already taken.
   */
  importedShowSlugs?: string[];
  /**
   * Every local persona's own slug, per `GET /api/personas` (SPEC F118.3, PLAN T255) — the "not
   * already hired" half of the soft "also hire ⟨persona⟩" offer's eligibility gate (the "on the
   * shelf" half reads `initialIndex.entries` directly, already in hand). Sourced from the SAME
   * listing `PersonasClient`'s own hired-state already reads, fetched afresh here since this is a
   * separate page/server component — mirrors `installedFontSlugs`'s own per-page-fetch shape rather
   * than threading persona state across a route boundary. Defaults to `[]` — fail closed: no live
   * signal never CLAIMS a persona is already hired, which would silently WITHHOLD an offer the
   * operator should have seen (the safer failure direction here is the opposite of the installed-
   * state props above, but the same "no live signal, no false claim" posture).
   */
  hiredPersonaSlugs?: string[];
  /**
   * Every already-installed avatar pack's slug (PLAN T294) — mirrors `installedFontSlugs`'s own
   * "reopening shows no installed state" fix (gh-#375/PLAN T204), applied to the avatar kind per
   * this task's own "match the font install flow exactly" instruction. Sourced from
   * `GET /api/avatar-packs` (PLAN T294's own listing route). Defaults to `[]` — fail closed, the
   * same posture every other installed-slugs prop on this component carries.
   */
  installedAvatarSlugs?: string[];
  /**
   * Every already-installed icon pack's slug (PLAN T304) — mirrors `installedAvatarSlugs`'s own
   * shape verbatim, a different endpoint (`GET /api/icon-packs`). Defaults to `[]` — fail closed,
   * the same posture every other installed-slugs prop on this component carries.
   */
  installedIconSlugs?: string[];
  /** Test-only injection point for the theme provenance line's `formatDateStamp` call (gh-#375);
   * production omits this and gets the browser's local zone — the same SettingsForm/WardrobeClient/
   * PersonasClient idiom, not a bespoke one. */
  timeZone?: string;
  /**
   * The kind tab this render shows (gh-#372) — resolved off `?kind=` by the page's own server
   * component (`PersonaCatalogTabs.resolveCatalogKind`). Filters the GRID (and the inline detail
   * section under it) alone: every cross-kind input — the soft hire-offer eligibility, the show
   * review modal, the install-modal guards — keeps reading the FULL index, so an entry being off
   * the active tab never changes what the shelf KNOWS, only what it currently shows. Defaults to
   * `"persona"`, the shelf's founding kind and the bare-URL tab.
   */
  activeKind?: CatalogEntryKind;
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
export function PersonaCatalogClient({
  initialIndex,
  installedFontSlugs = [],
  installedThemeProvenance = [],
  importedShowSlugs = [],
  hiredPersonaSlugs = [],
  installedAvatarSlugs = [],
  installedIconSlugs = [],
  timeZone,
  activeKind = "persona",
}: PersonaCatalogClientProps): ReactNode {
  const router = useRouter();
  const [detail, setDetail] = useState<DetailState>({ kind: "idle" });
  // Which persona's review modal is open, keyed by SLUG — not a bare boolean (PLAN T255 review
  // finding F1, HIGH): the render gate below (`detail.kind === "loaded" && detail.slug ===
  // reviewingPersonaSlug`) requires an EXACT match against whatever `detail` currently holds, so a
  // failed or superseded `loadDetail` call (a different card clicked while a fetch is still in
  // flight, or the offer's own suggested-persona fetch failing outright) can never satisfy it — the
  // modal simply never opens for the wrong persona, or for none at all. A bare boolean (the
  // original shape) had neither property: once armed by `handleAcceptPersonaOffer` it stayed armed
  // through a failed fetch, so the NEXT successfully-loaded card of ANY kind — not just the one the
  // offer named — popped the review modal open under it.
  const [reviewingPersonaSlug, setReviewingPersonaSlug] = useState<string | null>(null);
  const [installingTheme, setInstallingTheme] = useState(false);
  const [installingFont, setInstallingFont] = useState(false);
  const [installingAvatar, setInstallingAvatar] = useState(false);
  const [installingIcon, setInstallingIcon] = useState(false);
  const [installingAdPack, setInstallingAdPack] = useState(false);
  // Which show entry (if any) has its combined detail/review modal open (PLAN T255) — a show never
  // routes through `detail`/`loadDetail` at all (see `ShowCardReviewModal`'s own remarks for why),
  // so this is its own, independent piece of state.
  const [reviewingShowSlug, setReviewingShowSlug] = useState<string | null>(null);
  // The pending soft "also hire ⟨persona⟩" offer (SPEC F118.3, PLAN T255) — `null` until a show
  // import succeeds AND names an eligible `suggestedPersona` (see `handleShowImported`'s own
  // remarks). A dedicated, file-local dialog (`PersonaOfferDialog` below) rather than the shared
  // `useConfirm()` hook (T255 review note): this component already renders unconditionally on
  // several existing spec harnesses with no `ConfirmDialogProvider` ancestor — a `useConfirm()` call
  // here would throw on every one of them. `PersonaOfferDialog` needs no such ancestor.
  const [personaOffer, setPersonaOffer] = useState<{ suggestedSlug: string; showName: string } | null>(null);
  // Seeded from the server-fetched prop above, then flipped locally the instant an install
  // succeeds (handleFontInstalled below) — cheap, no reload/re-fetch needed for a set this small.
  // `useState(() => ...)` (lazy initializer): this only needs to run once, not re-derive the Set on
  // every render.
  const [installedSlugs, setInstalledSlugs] = useState<ReadonlySet<string>>(() => new Set(installedFontSlugs));
  // Same lazy-initializer/local-flip shape as `installedSlugs` above (PLAN T294) — flipped the
  // instant an avatar pack install succeeds (handleAvatarInstalled below), so reopening that same
  // slug reads "Installed"/"Re-install" with no reload.
  const [installedAvatarPackSlugs, setInstalledAvatarPackSlugs] = useState<ReadonlySet<string>>(
    () => new Set(installedAvatarSlugs)
  );
  // Same lazy-initializer/local-flip shape as `installedAvatarPackSlugs` above (PLAN T304) —
  // flipped the instant an icon pack install succeeds (handleIconInstalled below), so reopening
  // that same slug reads "Installed"/"Re-install" with no reload.
  const [installedIconPackSlugs, setInstalledIconPackSlugs] = useState<ReadonlySet<string>>(
    () => new Set(installedIconSlugs)
  );
  // Same lazy-initializer/local-flip shape as `installedSlugs` above, keyed by slug — a Map, not a
  // Set, because the theme detail panel's provenance line needs the WHOLE row
  // (importedFrom/importedAt), not just a boolean.
  const [installedThemes, setInstalledThemes] = useState<ReadonlyMap<string, ThemeCatalogProvenanceDto>>(
    () => new Map(installedThemeProvenance.map((provenance) => [provenance.slug, provenance]))
  );
  // Same lazy-initializer/local-flip shape as `installedSlugs` above (PLAN T255) — flipped the
  // instant a show import succeeds (handleShowImported below), so re-opening that same slug reads
  // "Imported" with no reload.
  const [importedShows, setImportedShows] = useState<ReadonlySet<string>>(() => new Set(importedShowSlugs));
  // Never locally flipped (contrast `installedSlugs`/`importedShows` above): a persona hired via the
  // soft offer below navigates away (`handleImported`'s own `router.push("/personas")`) before a
  // second offer in the same session could ever matter — see `handleShowImported`'s own remarks.
  const hiredPersonaSlugSet = useMemo(() => new Set(hiredPersonaSlugs), [hiredPersonaSlugs]);

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
    setReviewingPersonaSlug(null);
    toast.success(`"${result.name}" ${result.created ? "hired" : "updated"}.`);
    for (const warning of result.warnings) toast.error(warning);
    router.push("/personas");
  }

  /** SPEC F103.6's success path: no `/themes` list page exists to land on (unlike Personas' own
   * `router.push` above) — `Station:Theme`'s choice list widening is a server-side fact the next
   * `GET /api/settings` read already reflects (PLAN T183/T184). Closing the modal, toasting, AND
   * (gh-#375 — mirrors `handleFontInstalled`'s own local flip) marking `slug` installed
   * in local state — so `ThemeDetailPanel` flips to "Installed"/"Re-install" with the real
   * provenance line immediately, no reload — is the whole client-side job. A failed install never
   * reaches this function at all (`ThemeInstallModal` only calls `onInstalled` on its own 2xx
   * branch), so a rejected confirm flips nothing here, same as the font half. */
  function handleThemeInstalled(slug: string, result: ThemeInstallResult): void {
    setInstallingTheme(false);
    setInstalledThemes((prev) => {
      const next = new Map(prev);
      next.set(slug, { slug, importedFrom: result.importedFrom, importedAt: result.importedAt });
      return next;
    });
    toast.success(`"${result.name}" installed.`);
  }

  /** SPEC F104.5's success path — mirrors `handleThemeInstalled`'s own remarks: no dedicated
   * wardrobe-list page exists on THIS task's own owned files for the panel to route to (PLAN T203
   * builds that separately); closing the modal, toasting the family that just entered the station's
   * Wardrobe, AND (PLAN T204) marking `slug` installed in local state — so `FontDetailPanel` flips
   * to "Installed"/"Re-install" immediately, no reload — is the whole client-side job. */
  function handleFontInstalled(slug: string, result: FontInstallResult): void {
    setInstallingFont(false);
    setInstalledSlugs((prev) => new Set(prev).add(slug));
    toast.success(`"${result.family}" installed.`);
  }

  /**
   * SPEC F128.3's success path — mirrors `handleFontInstalled`'s own shape exactly (this task's own
   * "match the font install flow" instruction): closes the modal, marks `slug` installed in local
   * state so `AvatarDetailPanel` flips to "Installed"/"Re-install" immediately (no reload), and
   * toasts the pack's own manifest name. `clampPackDisplayText` (PLAN T294 rider 2) bounds that name
   * before it reaches the toast — `AvatarPackController.Install` never bounds `manifest.PackName`'s
   * own length itself (see `lib/clamp-pack-display-text.ts`'s own remarks), so this is layout
   * protection for the toast, not a security boundary (React/the toast layer already escape the
   * content either way).
   */
  function handleAvatarInstalled(slug: string, result: AvatarInstallResult): void {
    setInstallingAvatar(false);
    setInstalledAvatarPackSlugs((prev) => new Set(prev).add(slug));
    toast.success(`"${clampPackDisplayText(result.packName)}" installed.`);
  }

  /**
   * SPEC F130.5's success path — mirrors `handleAvatarInstalled`'s own shape exactly, the icon-kind
   * sibling: closes the modal, marks `slug` installed in local state so `IconDetailPanel` flips to
   * "Installed"/"Re-install" immediately (no reload). Toasts the icon count, not a pack NAME (SPEC
   * F130.1's `gw-icon-pack` document carries no pack-level display name at all — see
   * `IconPackSummaryDto`'s own remarks) — the same "smallest honest surface" this kind's every other
   * UI treatment already follows.
   */
  function handleIconInstalled(slug: string, result: IconInstallResult): void {
    setInstallingIcon(false);
    setInstalledIconPackSlugs((prev) => new Set(prev).add(slug));
    toast.success(`Icon pack "${slug}" installed (${result.iconCount} icon${result.iconCount === 1 ? "" : "s"}).`);
  }

  /**
   * SPEC F162.2's success path — mirrors `handleIconInstalled`'s own shape, minus the local
   * installed-slug flip: this kind carries no dedicated per-pack listing endpoint this task adds
   * (`AdPackController`'s own class remarks), so `AdPackDetailPanel` has no "Installed" chip to flip
   * in the first place — closing the modal and toasting the brief count is the whole client-side
   * job. Toasts the pack's own display name when present, falling back to the brief count alone
   * (SPEC F162.2's own `packName` is genuinely optional on this kind, unlike an avatar pack's own
   * required one).
   */
  function handleAdPackInstalled(result: AdPackInstallResult): void {
    setInstallingAdPack(false);
    const briefCount = result.brands.length;
    const briefWord = `${briefCount} brief${briefCount === 1 ? "" : "s"}`;
    toast.success(
      result.packName !== null
        ? `"${clampPackDisplayText(result.packName)}" installed (${briefWord}).`
        : `Ad pack installed (${briefWord}).`
    );
  }

  /**
   * A show entry's OPTIONAL `suggestedPersona` (SPEC F118.3, PLAN T255) is on the shelf when the
   * ALREADY-fetched index carries a persona entry under that exact slug — never a further catalog
   * fetch just to answer this. An absent/unknown suggestion (no such persona entry at all) reads
   * `false` here the same as one that's on the shelf but already hired — `handleShowImported`'s
   * caller doesn't need to distinguish the two, both mean "no offer, no error" (SPEC F118.3).
   */
  function suggestedPersonaIsOfferable(suggestedPersona: string): boolean {
    const onShelf = entries.some((entry) => entry.kind === "persona" && entry.slug === suggestedPersona);
    return onShelf && !hiredPersonaSlugSet.has(suggestedPersona);
  }

  /**
   * SPEC F118.2's success path: closes the review modal, marks the slug imported locally (gh-#375's
   * "reopening shows no installed state" fix, applied here — mirrors `handleFontInstalled`'s own
   * local flip), and toasts. Then SPEC F118.3's soft offer: eligible only when `suggestedPersona` is
   * present, on the shelf, and not already hired (`suggestedPersonaIsOfferable` above) arms
   * `personaOffer`, which `PersonaOfferDialog` below renders — a plain yes/no with a plain-words
   * consequence, not rich content to review (that review already happened for the SHOW above, and
   * happens again, in full, for the PERSONA once accepted).
   */
  function handleShowImported(result: ShowCardReviewImportResult): void {
    setReviewingShowSlug(null);
    setImportedShows((prev) => new Set(prev).add(result.slug));
    toast.success(`"${result.name}" imported.`);

    const suggested = result.suggestedPersona;
    if (suggested === null || !suggestedPersonaIsOfferable(suggested)) return;

    setPersonaOffer({ suggestedSlug: suggested, showName: result.name });
  }

  /**
   * Accepting the soft offer (SPEC F118.3) reuses the EXISTING persona import flow verbatim: `loadDetail`
   * is the SAME fetch a click on that persona's own shelf card already triggers, and
   * `setReviewingPersonaSlug(suggestedSlug)` arms the SAME `PersonaCardReviewModal` `DetailPanel`'s
   * own Hire button arms — the full-card trust ruling for the PERSONA'S card is never skipped just
   * because the offer that led here was itself a simple yes/no. This is the smaller, house-
   * consistent shape: zero new import-chaining logic, one `fetch` this file already owns, one modal
   * this file already renders.
   *
   * Arming BEFORE `loadDetail` resolves is safe (PLAN T255 review finding F1): the render gate
   * below only ever opens the modal once `detail.kind === "loaded" && detail.slug ===
   * reviewingPersonaSlug` — a failed fetch leaves `detail.kind` at `"error"`, which can never
   * satisfy that gate, and a DIFFERENT card clicked before this one resolves moves `detail.slug`
   * off `suggestedSlug` entirely. Nothing else needs to react to failure here.
   *
   * `hiredPersonaSlugSet` is never locally updated after this (contrast `importedShows`/
   * `installedSlugs` elsewhere in this file): `handleImported`'s own `router.push("/personas")`
   * navigates the operator off this page the instant that second hire completes, so a second offer
   * naming the SAME persona could only ever re-arise within an already-superseded render of this
   * page — tracking it would be dead code, not a real fix (YAGNI).
   */
  function handleAcceptPersonaOffer(): void {
    if (personaOffer === null) return;
    const { suggestedSlug } = personaOffer;
    setPersonaOffer(null);
    void loadDetail(suggestedSlug);
    setReviewingPersonaSlug(suggestedSlug);
  }

  /** Declining leaves the show imported and hires nothing (SPEC F118.3) — the offer simply closes,
   * no request of any kind; the show import already committed before this offer ever appeared. */
  function handleDeclinePersonaOffer(): void {
    setPersonaOffer(null);
  }

  const selectedSlug = detail.kind !== "idle" ? detail.slug : null;
  const selectedEntry = entries.find((entry) => entry.slug === selectedSlug) ?? null;

  // The active tab's own slice of the shelf (gh-#372) — the ONLY place `activeKind` narrows
  // anything rendered as the grid. `entries` stays the source for every cross-kind lookup above.
  const visibleEntries = entries.filter((entry) => entry.kind === activeKind);

  // A tab switch keeps this client mounted (only the server component re-renders with a new
  // `activeKind` prop), so an inline detail panel opened on ANOTHER tab must hide with its grid
  // rather than dangling under a kind it doesn't belong to — the operator lands back on it intact
  // by switching back. The overlay modals (show review, install confirms) deliberately do NOT get
  // this gate: they're blocking flows mid-decision, not tab content. The `reviewingPersonaSlug`
  // exemption is the same ruling applied to the soft hire offer (SPEC F118.3): accepting it loads a
  // PERSONA detail while the operator stands on the Shows tab, and its failure path reports through
  // this very section (T255 review finding F1's inline error) — hiding that would swallow the one
  // signal the operator gets that the offered persona failed to load.
  const selectedOnActiveTab =
    selectedEntry !== null
    && (selectedEntry.kind === activeKind || (detail.kind !== "idle" && detail.slug === reviewingPersonaSlug));

  /** Routes one shelf entry to its kind's own card (review finding, T185): an exhaustive `switch`
   * over `kind`, not a two-way ternary — the SERVER already drops any kind it doesn't recognise
   * (CatalogIndexValidator, F103.1/AC6), but a ternary's `else` branch would silently render an
   * unrecognised future kind AS a persona card. The `default` here renders nothing instead, so the
   * client never lies about an entry it can't actually route, should that server invariant ever
   * slip. */
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
      case "avatar":
        return (
          <KindMarkerShelfCard
            key={entry.slug}
            entry={entry}
            marker="Avatar pack"
            selected={entry.slug === selectedSlug}
            onSelect={() => handleCardClick(entry.slug)}
          />
        );
      case "icon":
        return (
          <KindMarkerShelfCard
            key={entry.slug}
            entry={entry}
            marker="Icon pack"
            selected={entry.slug === selectedSlug}
            onSelect={() => handleCardClick(entry.slug)}
          />
        );
      case "ad-pack":
        return (
          <KindMarkerShelfCard
            key={entry.slug}
            entry={entry}
            marker="Ad pack"
            selected={entry.slug === selectedSlug}
            onSelect={() => handleCardClick(entry.slug)}
          />
        );
      case "show":
        // Deliberately NOT `handleCardClick`/`detail` (PLAN T255) — a show card opens its own
        // combined detail-and-review modal directly (see `ShowCardReviewModal`'s own remarks), so
        // it never enters the `selected`/inline-panel state the other three kinds share.
        return (
          <ShowShelfCard
            key={entry.slug}
            entry={entry}
            imported={importedShows.has(entry.slug)}
            onSelect={() => setReviewingShowSlug(entry.slug)}
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
   * `default`, rendering nothing at all. No `"show"` arm (PLAN T255): a show entry never sets
   * `detail.kind` to `"loaded"` in the first place (its own card routes through `reviewingShowSlug`
   * instead, see `renderShelfEntry`'s own `"show"` case) — this switch's `default` arm is simply
   * never reached for that kind, not a gap. */
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
            provenance={installedThemes.get(loaded.slug) ?? null}
            timeZone={timeZone}
            onInstallClick={() => setInstallingTheme(true)}
          />
        );
      case "persona":
        return (
          <DetailPanel
            slug={loaded.slug}
            detail={loaded.detail}
            onImportClick={() => setReviewingPersonaSlug(loaded.slug)}
          />
        );
      case "font":
        return (
          <FontDetailPanel
            slug={loaded.slug}
            detail={loaded.detail}
            isInstalled={installedSlugs.has(loaded.slug)}
            onInstallClick={() => setInstallingFont(true)}
          />
        );
      case "avatar":
        return (
          <AvatarDetailPanel
            slug={loaded.slug}
            detail={loaded.detail}
            isInstalled={installedAvatarPackSlugs.has(loaded.slug)}
            onInstallClick={() => setInstallingAvatar(true)}
          />
        );
      case "icon":
        return (
          <IconDetailPanel
            slug={loaded.slug}
            detail={loaded.detail}
            isInstalled={installedIconPackSlugs.has(loaded.slug)}
            onInstallClick={() => setInstallingIcon(true)}
          />
        );
      case "ad-pack":
        return (
          <AdPackDetailPanel
            slug={loaded.slug}
            detail={loaded.detail}
            onInstallClick={() => setInstallingAdPack(true)}
          />
        );
      default:
        return null;
    }
  }

  return (
    <div>
      {visibleEntries.length === 0 ? (
        // This kind's tab is empty while the shelf itself is not (the whole-index empty state
        // already returned above) — name the kind, no CTA: there is nothing to install and nothing
        // to configure, the shelf just hasn't stocked this kind yet (gh-#372, the gh-#393
        // empty-tabs ruling).
        <EmptyState
          title={`No ${KIND_TAB_NOUN[activeKind]} on the shelf`}
          reason="The shelf will stock this kind when the community catalog gains entries for it."
        />
      ) : (
        <ul aria-label="Community catalog entries" className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {visibleEntries.map(renderShelfEntry)}
        </ul>
      )}

      {detail.kind !== "idle" && selectedOnActiveTab && (
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
          formality, not a real runtime path.

          `detail.slug === reviewingPersonaSlug` (PLAN T255 review finding F1, HIGH), not a bare
          `reviewingPersonaSlug !== null` check: `reviewingPersonaSlug` can be armed (by
          `handleAcceptPersonaOffer`) BEFORE `detail` ever reflects that slug, and `detail` can move
          on to a DIFFERENT slug (another card clicked) before that fetch resolves — this exact
          match is what stops either race from popping the modal open for the wrong persona, or for
          a persona whose own fetch failed outright (`detail.kind` would be `"error"`, never
          `"loaded"`, for that slug). */}
      {detail.kind === "loaded" && detail.slug === reviewingPersonaSlug && detail.detail.card !== null && (
        <PersonaCardReviewModal
          cardText={detail.detail.card}
          catalogSlug={detail.slug}
          avatarFile={detail.detail.personaAvatarFile}
          samples={detail.detail.samplePatter ?? []}
          verb="hire"
          onCancel={() => setReviewingPersonaSlug(null)}
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
          onInstalled={(result) => handleThemeInstalled(detail.slug, result)}
        />
      )}

      {/* Cancel = no-op (SPEC F104.5's own sad path, mirrors the theme block above): closing this
          modal by any path just resets `installingFont`, never touching the network — see
          FontInstallModal's own remarks. No `detail.detail.card !== null` guard needed here (unlike
          the theme block above): FontInstallModal posts no body of its own, so it has nothing to
          read off `detail.detail.card` at all — only `selectedEntry?.kind === "font"` gates it. */}
      {installingFont && detail.kind === "loaded" && selectedEntry?.kind === "font" && (
        <FontInstallModal
          slug={detail.slug}
          onCancel={() => setInstallingFont(false)}
          onInstalled={(result) => handleFontInstalled(detail.slug, result)}
        />
      )}

      {/* Cancel = no-op (mirrors the font block immediately above, the SAME "no request body" shape
          — AvatarInstallModal posts no body of its own either, so only `selectedEntry?.kind ===
          "avatar"` gates it, no `detail.detail.card !== null` guard needed). */}
      {installingAvatar && detail.kind === "loaded" && selectedEntry?.kind === "avatar" && (
        <AvatarInstallModal
          slug={detail.slug}
          onCancel={() => setInstallingAvatar(false)}
          onInstalled={(result) => handleAvatarInstalled(detail.slug, result)}
        />
      )}

      {/* Cancel = no-op (mirrors the avatar block immediately above, the SAME "no request body"
          shape — IconInstallModal posts no body of its own either). */}
      {installingIcon && detail.kind === "loaded" && selectedEntry?.kind === "icon" && (
        <IconInstallModal
          slug={detail.slug}
          onCancel={() => setInstallingIcon(false)}
          onInstalled={(result) => handleIconInstalled(detail.slug, result)}
        />
      )}

      {/* Cancel = no-op (mirrors the icon block immediately above, the SAME "no request body"
          shape — AdPackInstallModal posts no body of its own either). */}
      {installingAdPack && detail.kind === "loaded" && selectedEntry?.kind === "ad-pack" && (
        <AdPackInstallModal
          slug={detail.slug}
          onCancel={() => setInstallingAdPack(false)}
          onInstalled={handleAdPackInstalled}
        />
      )}

      {/* Cancel = no-op (mirrors the theme/font blocks above): closing this modal by any path just
          resets `reviewingShowSlug`, never touching the network — see ShowCardReviewModal's own
          remarks. Independent of `detail`/`selectedEntry` entirely (PLAN T255) — this modal owns
          its own entry fetch keyed on `reviewingShowSlug` alone. */}
      {reviewingShowSlug !== null && (
        <ShowCardReviewModal
          slug={reviewingShowSlug}
          alreadyImported={importedShows.has(reviewingShowSlug)}
          onCancel={() => setReviewingShowSlug(null)}
          onImported={handleShowImported}
        />
      )}

      {/* SPEC F118.3's soft offer — see `handleShowImported`'s own remarks for the eligibility
          gate and `handleAcceptPersonaOffer`'s own remarks for why accepting reuses the persona
          import flow verbatim rather than posting anything itself. */}
      {personaOffer !== null && (
        <PersonaOfferDialog
          suggestedSlug={personaOffer.suggestedSlug}
          showName={personaOffer.showName}
          onAccept={handleAcceptPersonaOffer}
          onDecline={handleDeclinePersonaOffer}
        />
      )}
    </div>
  );
}

/**
 * The soft "also hire ⟨persona⟩" offer's own plain yes/no dialog (SPEC F118.3, PLAN T255) — a
 * small, FILE-LOCAL dialog rather than the shared `useConfirm()` hook (T255 review note):
 * `PersonaCatalogClient` renders unconditionally on several existing spec harnesses with no
 * `ConfirmDialogProvider` ancestor, so a `useConfirm()` call inside that component would throw on
 * every one of them. This component needs no such ancestor: it owns its own state and shares only
 * the presentational chrome with `ConfirmDialogProvider`'s dialog, via `DialogShell` (PLAN T255
 * review finding F4 — the same "extract, don't duplicate" reasoning `catalog-badges.tsx` already
 * applies one level up). Accepting/declining are pure state transitions the PARENT performs
 * (`onAccept`/`onDecline`), mirroring `FireModal`'s own "no request of its own" shape one level up.
 */
function PersonaOfferDialog({
  suggestedSlug,
  showName,
  onAccept,
  onDecline,
}: {
  suggestedSlug: string;
  showName: string;
  onAccept: () => void;
  onDecline: () => void;
}): ReactNode {
  // Hand-wired focus restoration (the shared `useRestoreFocus` hook, gh-#465): this component
  // mounts fresh with no real `Dialog.Trigger` of its own, so Radix has nothing to auto-refocus.
  // Owned HERE, not by `DialogShell` (see that component's own remarks on why the capture timing
  // can't be centralised between it and `ConfirmDialogProvider`'s own always-mounted dialog —
  // the hook's `"on-mount"` vs `"imperative"` split).
  const restoreFocus = useRestoreFocus("on-mount");

  return (
    <DialogShell
      open
      onOpenChange={(open) => {
        if (!open) onDecline();
      }}
      onCloseAutoFocus={restoreFocus.onCloseAutoFocus}
    >
      <Dialog.Title className="font-display text-[1.1rem] text-ink">
        {`Also hire "${prettifySlug(suggestedSlug)}"?`}
      </Dialog.Title>
      <Dialog.Description className="mt-2 text-[0.85rem] text-mute">
        {`"${showName}" suggests pairing with this persona. Nothing is hired until you review its own card and confirm.`}
      </Dialog.Description>
      <div className="mt-6 flex justify-end gap-2">
        <Button variant="secondary" onClick={onDecline}>
          No thanks
        </Button>
        <Button variant="primary" onClick={onAccept}>
          Review persona
        </Button>
      </div>
    </DialogShell>
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
    case "avatar":
      return "Avatar pack details";
    case "icon":
      return "Icon pack details";
    case "ad-pack":
      return "Ad pack details";
    case "persona":
    default:
      return "Persona details";
  }
}

/**
 * A theme entry's detail panel (SPEC F103.5, F103.6, PLAN T186; installed-state awareness gh-#375
 * — the theme half of Dean's demo feedback, mirroring `FontDetailPanel`'s own
 * `isInstalled`/Re-install treatment). `provenance` is `null` for a theme with no `station.theme`
 * row under this catalog slug (never installed, or the shipped default it happens to share a slug
 * with — see `ThemeCatalogProvenanceDto`'s own remarks); non-null drives the SAME "Installed" chip
 * `FontDetailPanel` uses (the shared `Chip` component) plus an "Imported · ⟨source⟩ · ⟨date⟩"
 * provenance line — the T187 copy verbatim, minus the leading label `SettingsForm`'s own
 * `ThemeProvenanceBadge` folds in (this panel already names the theme in its own heading, exactly
 * the same reasoning `FontDetailPanel`'s own bare-word chip gives) — and the Install→Re-install
 * button label. `importedFrom` renders VERBATIM, same provenance rule every other chip in this
 * codebase follows.
 */
function ThemeDetailPanel({
  slug,
  manifestText,
  provenance,
  timeZone,
  onInstallClick,
}: {
  slug: string;
  manifestText: string;
  provenance: ThemeCatalogProvenanceDto | null;
  timeZone?: string;
  onInstallClick: () => void;
}): ReactNode {
  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="font-display text-[1.1rem] text-ink">{prettifySlug(slug)}</h2>
          {provenance !== null && <Chip>Installed</Chip>}
        </div>
        {/* Install/Re-install (SPEC F103.6; label gh-#375) opens ThemeInstallModal's
            confirm/cancel step — this click itself issues no request; the modal POSTs the SAME
            manifestText already reviewed here. Re-install is a genuinely supported, non-destructive
            action — ThemesImportController.Import upserts by slug (SPEC F103.7). */}
        <Button type="button" variant="primary" onClick={onInstallClick}>
          {provenance !== null ? "Re-install" : "Install"}
        </Button>
      </div>

      {provenance !== null && (
        <p className="text-[0.75rem] text-mute">
          {`Imported · ${provenance.importedFrom} · ${formatDateStamp(provenance.importedAt, { timeZone })}`}
        </p>
      )}

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

/**
 * The shared "kind-marker" shelf card shape (T405 review F7 fold): `AvatarShelfCard`/`IconShelfCard`/
 * `AdPackShelfCard` were three byte-identical copies of this exact markup, differing only in their
 * own kind-marker text ("Avatar pack"/"Icon pack"/"Ad pack") — none of the three kinds' own
 * `CatalogShelfEntryDto` carries a kind-specific shelf field to paint (see each former component's
 * own now-removed remarks for why: an avatar/icon/ad pack's own display name/count lives only on
 * the DETAIL wire, never the zero-cost INDEX row this card paints from), so there was never a real
 * per-kind difference here to keep as three separate components. A title (slug-derived — none of
 * these three kinds has an index-level display name to prefer over it), the 18+ badge, one
 * `marker` line, and `bestFor` chips — all painted straight off the entry's already-fetched index
 * row, no manifest or asset fetch, ever, while browsing (the SAME zero-cost-browse contract every
 * kind's shelf card holds to).
 */
function KindMarkerShelfCard({
  entry,
  marker,
  selected,
  onSelect,
}: {
  entry: CatalogShelfEntryDto;
  marker: string;
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
        <p className="text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2">{marker}</p>
        <BestForChips items={entry.bestFor} />
      </button>
    </li>
  );
}

/**
 * A show entry's shelf card (SPEC F118.1, PLAN T255) — name (title-cased slug, the same fallback
 * every other kind's card uses — see `ShelfCard`'s own precedent) and `bestFor` chips, both painted
 * straight off the entry's already-fetched index row, NO manifest fetch while browsing (the shelf's
 * own zero-cost-browse contract every other kind already holds to). Tagline/flavor are NOT here:
 * `CatalogShelfEntryDto` carries no such field for any kind (verified against the T254 wire this
 * task builds against — those two only ever live inside the manifest text `GET
 * /api/catalog/entries/{slug}` fetches, T255's own dispatch note flagged this as worth checking) —
 * they render inside `ShowCardReviewModal` instead, the one place this kind pays that fetch.
 *
 * Not a toggling `aria-expanded` button like `ShelfCard`/`ThemeShelfCard`/`FontShelfCard` (PLAN
 * T255): a click here opens `ShowCardReviewModal` directly, a genuinely different interaction
 * (a dialog, not an inline expand/collapse panel) — `aria-haspopup="dialog"` names that honestly.
 */
function ShowShelfCard({
  entry,
  imported,
  onSelect,
}: {
  entry: CatalogShelfEntryDto;
  imported: boolean;
  onSelect: () => void;
}): ReactNode {
  return (
    <li>
      <button
        type="button"
        onClick={onSelect}
        aria-haspopup="dialog"
        className="flex w-full flex-col items-start gap-2 rounded-[6px] border border-line bg-surface p-4 text-left transition-colors duration-[120ms] ease-out hover:bg-surface-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
      >
        <div className="flex w-full items-center justify-between gap-2">
          <span className="font-display text-[1.05rem] text-ink">{prettifySlug(entry.slug)}</span>
          <div className="flex items-center gap-2">
            {/* Installed-state honesty (PLAN T255, the font/theme gh-#375 precedent, applied to
                shows): an already-imported slug says so right on the shelf, never left to be
                discovered only after re-opening the review modal. */}
            {imported && <Chip>Imported</Chip>}
            {entry.audience === "mature" && <MatureBadge />}
          </div>
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

