// @jest-environment jsdom
// STORY-315 — Hire a show from the shelf (SPEC F118.1, F118.2, F118.3)
//
// Runner: Jest. The community-catalog shelf gains a fourth kind: show entries are listed on the
// SAME shelf as personas/themes/fonts, routed by `kind` (mirrors theme-catalog-shelf.spec.tsx's own
// T185 harness) — but a show card opens a COMBINED detail-and-review modal directly (no inline
// panel step) since `CatalogShelfEntryDto` carries no tagline for any kind (verified against the
// T254 wire this task builds against — only slug/audience/bestFor cost nothing to browse; name and
// tagline live in the manifest text the modal's own `GET /api/catalog/entries/{slug}` fetch reads).
// Confirming posts the manifest to `POST /api/shows/{slug}/import`; a successful import may then
// offer to hire the manifest's own OPTIONAL `suggestedPersona` (SPEC F118.3) — a soft, declinable
// prompt that, if accepted, reuses the EXISTING persona review-then-hire flow (`PersonaCardReviewModal`)
// rather than importing a second time silently.
//
// RTL drives PersonaCatalogClient directly with a fake initialIndex (mirrors
// persona-catalog-page.spec.tsx's own "Feature: Browsing the shelf" block). `next/navigation` is
// mocked (PersonaCatalogClient calls useRouter() unconditionally since PLAN T103) and the
// component is dynamically imported AFTER that mock registers — see
// persona-catalog-page.spec.tsx's own remarks on why a static top-level import would bind the
// REAL next/navigation export first under this project's SWC-based jest transform.

jest.mock("next/navigation", () => ({
  ...jest.requireActual<typeof import("next/navigation")>("next/navigation"),
  useRouter: jest.fn(),
}));

import { describe, it, expect, jest, beforeAll, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent, waitFor, within, act, type RenderResult } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { useRouter } from "next/navigation";
import { Toaster } from "@/components/ui/toast";
import type { PersonaCatalogClient as PersonaCatalogClientComponent } from "../app/(authed)/persona-catalog/PersonaCatalogClient";
import type { CatalogEntryDetailDto, CatalogShelfEntryDto } from "../app/(authed)/persona-catalog/types";

const mockedUseRouter = jest
  .requireMock<{ useRouter: typeof useRouter }>("next/navigation")
  .useRouter as jest.MockedFunction<typeof useRouter>;

let PersonaCatalogClient: typeof PersonaCatalogClientComponent;

beforeAll(async () => {
  ({ PersonaCatalogClient } = await import("../app/(authed)/persona-catalog/PersonaCatalogClient"));
});

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SHOW_ENTRY: CatalogShelfEntryDto = {
  slug: "morning-drive",
  kind: "show",
  audience: "everyone",
  bestFor: ["morning", "upbeat"],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const SHOW_CARD_JSON = JSON.stringify({
  schemaVersion: 1,
  name: "Morning Drive",
  tagline: "Wake up right",
  flavor: "Upbeat, punchy, keep it moving before 9am — never mellow, never slow.",
});

/** A show detail carrying an eligible `suggestedPersona` ("flip") — the happy-path offer fixture. */
const SHOW_DETAIL_WITH_OFFERABLE_SUGGESTION: CatalogEntryDetailDto = {
  card: SHOW_CARD_JSON,
  meta: "{}",
  fetchedAt: "2026-08-10T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: ["morning", "upbeat"],
  author: "Test Author",
  description: "A high-energy wake-up show.",
  samplePatter: ["Rise and shine!"],
  fontFamily: null,
  fontByteTotal: null,
  fontSpecimenFile: null,
  fontLicense: null,
  fontVersion: null,
  fontSubset: null,
  suggestedPersona: "flip",
  avatarItems: null,
  personaAvatarFile: null,
  packName: null,
  iconCount: null,
};

const SHOW_DETAIL_NO_SUGGESTION: CatalogEntryDetailDto = {
  ...SHOW_DETAIL_WITH_OFFERABLE_SUGGESTION,
  suggestedPersona: null,
};

const SHOW_DETAIL_UNKNOWN_SUGGESTION: CatalogEntryDetailDto = {
  ...SHOW_DETAIL_WITH_OFFERABLE_SUGGESTION,
  suggestedPersona: "nobody-on-the-shelf",
};

const FLIP_PERSONA_ENTRY: CatalogShelfEntryDto = {
  slug: "flip",
  kind: "persona",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const FLIP_CARD_JSON = JSON.stringify({
  schemaVersion: 1,
  name: "Flip",
  tagline: "Turns the record over",
  soul: "A crate-digger who never repeats a set.",
  quirks: [],
  voice: { engine: "kokoro", voiceId: "af_flip", pace: 1.0, language: "en" },
  energyDisposition: 0.4,
  lore: [],
  corrections: [],
  taste: [],
});

const FLIP_DETAIL: CatalogEntryDetailDto = {
  card: FLIP_CARD_JSON,
  meta: "{}",
  fetchedAt: "2026-08-10T00:00:00Z",
  unreachable: false,
  audience: "everyone",
  bestFor: [],
  author: null,
  description: null,
  samplePatter: [],
  fontFamily: null,
  fontByteTotal: null,
  fontSpecimenFile: null,
  fontLicense: null,
  fontVersion: null,
  fontSubset: null,
  suggestedPersona: null,
  avatarItems: null,
  personaAvatarFile: null,
  packName: null,
  iconCount: null,
};

/** A SECOND, unrelated persona entry (PLAN T255 review finding F1) — "the next card clicked" in
 * the regression this fixture set pins: distinct from Flip in every way that matters to the
 * assertion (slug, name, card text), so a stray render of the WRONG review modal is unmistakable. */
const LENA_PERSONA_ENTRY: CatalogShelfEntryDto = {
  slug: "late-night-lena",
  kind: "persona",
  audience: "everyone",
  bestFor: [],
  preview: null,
  fontFamily: null,
  fontByteTotal: null,
};

const LENA_CARD_JSON = JSON.stringify({
  schemaVersion: 1,
  name: "Late Night Lena",
  tagline: "Warm 2am company",
  soul: "A late-night voice who never rushes a segue.",
  quirks: [],
  voice: { engine: "kokoro", voiceId: "af_lena", pace: 1.0, language: "en" },
  energyDisposition: -0.2,
  lore: [],
  corrections: [],
  taste: [],
});

const LENA_DETAIL: CatalogEntryDetailDto = { ...FLIP_DETAIL, card: LENA_CARD_JSON };

const ENTRY_URL = "/api/catalog/entries/morning-drive";
const FLIP_ENTRY_URL = "/api/catalog/entries/flip";
const LENA_ENTRY_URL = "/api/catalog/entries/late-night-lena";
const IMPORT_URL = "/api/shows/morning-drive/import?catalogSlug=morning-drive";
const FLIP_IMPORT_URL = "/api/personas/flip/import?catalogSlug=flip";

function makeJsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

/** Routes every request this feature's flow can issue to scriptable responses — anything else
 * throws, so a stray/unexpected request fails the test loudly rather than resolving silently. */
function showFlowFetchMock(
  overrides: {
    entry?: Response;
    importResponse?: Response;
    flipEntry?: Response;
    flipImportResponse?: Response;
    /** Lena's own entry fetch (PLAN T255 review finding F1's "click another card" step) — routed
     * only when a test actually exercises it; every other test never names Lena at all. */
    lenaEntry?: Response;
  } = {}
): jest.MockedFunction<typeof fetch> {
  return jest.fn<typeof fetch>().mockImplementation(async (input) => {
    const url = String(input);
    if (url === ENTRY_URL) return overrides.entry ?? makeJsonResponse(200, SHOW_DETAIL_WITH_OFFERABLE_SUGGESTION);
    if (url === IMPORT_URL) {
      return (
        overrides.importResponse ??
        makeJsonResponse(200, { slug: "morning-drive", name: "Morning Drive", tagline: "Wake up right", flavor: "…" })
      );
    }
    if (url === FLIP_ENTRY_URL) return overrides.flipEntry ?? makeJsonResponse(200, FLIP_DETAIL);
    if (url === FLIP_IMPORT_URL) {
      return overrides.flipImportResponse ?? makeJsonResponse(201, { name: "Flip", warnings: [] });
    }
    if (url === LENA_ENTRY_URL) return overrides.lenaEntry ?? makeJsonResponse(200, LENA_DETAIL);
    throw new Error(`unexpected fetch ${url}`);
  }) as unknown as jest.MockedFunction<typeof fetch>;
}

function cardFor(name: string): HTMLElement {
  const grid = screen.getByRole("list", { name: "Community catalog entries" });
  const nameNode = within(grid).getByText(name);
  const card = nameNode.closest("button");
  if (card === null) throw new Error(`No <button> ancestor for "${name}"`);
  return card;
}

/** Opens Morning Drive's combined detail/review modal and waits for its content to load. */
async function openMorningDriveReview(
  fetchMock: jest.MockedFunction<typeof fetch>,
  props: { entries?: CatalogShelfEntryDto[]; importedShowSlugs?: string[]; hiredPersonaSlugs?: string[] } = {}
): Promise<RenderResult> {
  global.fetch = fetchMock;
  const view = render(
    <>
      <PersonaCatalogClient activeKind="show"
        initialIndex={{
          entries: props.entries ?? [SHOW_ENTRY, FLIP_PERSONA_ENTRY],
          fetchedAt: "2026-08-10T00:00:00Z",
          unreachable: false,
        }}
        importedShowSlugs={props.importedShowSlugs ?? []}
        hiredPersonaSlugs={props.hiredPersonaSlugs ?? []}
      />
      <Toaster />
    </>
  );
  fireEvent.click(cardFor("Morning Drive"));
  // Bare role, no `{ name }` filter (house pattern — see theme-catalog-preview-install.spec.tsx's
  // own identical choice): Radix's `Dialog.Content` computes its accessible name off
  // `aria-labelledby` (pointing at the dynamic `Dialog.Title`), which wins over this component's
  // OWN static `aria-label="Review show"` per the ARIA accname algorithm — filtering by the static
  // label here would never match. Only one dialog is ever open at this point, so the bare role is
  // unambiguous.
  await screen.findByRole("dialog");
  await screen.findByText("Wake up right");
  return view;
}

/** Carries `openMorningDriveReview` through to a completed show import. Returns the render handle
 * (gh-#372) so a fact can `rerender` with a different `activeKind` — the same mounted client a real
 * tab navigation keeps. */
async function completeMorningDriveImport(
  fetchMock: jest.MockedFunction<typeof fetch>,
  props: { hiredPersonaSlugs?: string[]; entries?: CatalogShelfEntryDto[] } = {}
): Promise<RenderResult> {
  const view = await openMorningDriveReview(fetchMock, props);

  const dialog = within(screen.getByRole("dialog"));
  await act(async () => {
    fireEvent.click(dialog.getByRole("button", { name: /^Confirm (re-)?import$/ }));
    await Promise.resolve();
  });
  await waitFor(() => expect(screen.queryByText("The full show, exactly as authored. Nothing is imported until you confirm.")).not.toBeInTheDocument());
  return view;
}

describe("Feature: The show shelf", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUseRouter.mockReturnValue({ push: jest.fn() } as unknown as ReturnType<typeof useRouter>);
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: browsing show cards", () => {
    it("show cards render the entry's name and bestFor chips beside personas/themes/fonts, no fetch on browse", () => {
      const fetchMock = jest.fn<typeof fetch>();
      global.fetch = fetchMock as unknown as typeof fetch;

      render(
        <PersonaCatalogClient activeKind="show"
          initialIndex={{ entries: [SHOW_ENTRY], fetchedAt: "2026-08-10T00:00:00Z", unreachable: false }}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Morning Drive")).toBeInTheDocument();
      const bestFor = within(within(grid).getByRole("list", { name: "Best for" }));
      expect(bestFor.getByText("morning")).toBeInTheDocument();
      expect(bestFor.getByText("upbeat")).toBeInTheDocument();
      // Rendering the shelf alone — no click — must never touch the network: the card is painted
      // straight off the already-fetched index row (SPEC F90.2's zero-cost-browse contract).
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it("the detail modal shows the FULL card, including a visually explicit Flavor section, before confirm (F90 trust posture)", async () => {
      const fetchMock = showFlowFetchMock();
      await openMorningDriveReview(fetchMock);

      const dialog = within(screen.getByRole("dialog"));
      expect(dialog.getByText("Morning Drive")).toBeInTheDocument();
      expect(dialog.getByText("Wake up right")).toBeInTheDocument();
      expect(dialog.getByText("Flavor (feeds the DJ's prompt)")).toBeInTheDocument();
      expect(
        dialog.getByText("Upbeat, punchy, keep it moving before 9am — never mellow, never slow.")
      ).toBeInTheDocument();

      // Opening/loading the review issues no import request of any kind — only Confirm does.
      expect(fetchMock.mock.calls.some(([url]) => String(url) === IMPORT_URL)).toBe(false);
    });
  });

  describe("Scenario: the rotation rule in the confirm (SPEC F152.6, PLAN T363)", () => {
    it("renders 'aired 0 times' — never 'at most' — for the exact-zero ceiling", async () => {
      const fetchMock = showFlowFetchMock({
        entry: makeJsonResponse(200, {
          ...SHOW_DETAIL_WITH_OFFERABLE_SUGGESTION,
          card: JSON.stringify({
            schemaVersion: 1,
            name: "Morning Drive",
            tagline: "Wake up right",
            flavor: "Upbeat, punchy, keep it moving before 9am — never mellow, never slow.",
            envelope: { rotation: { maxPlays: 0, notAiredWithinDays: 30 } },
          }),
        }),
      });
      await openMorningDriveReview(fetchMock);

      expect(
        screen.getByText("Plays tracks aired 0 times and not aired in the last 30 days")
      ).toBeInTheDocument();
    });

    // PLAN T363 review LOW-1: maxPlays is a CEILING (play_count <= maxPlays), not an exact count —
    // "aired 3 times" would misstate a track that has aired anywhere from 0 to 3 times as having
    // aired exactly 3. Pins the "at most" wording for the N > 0 case the zero-case fact above cannot
    // exercise (0 reads identically under either wording).
    it("renders 'aired at most N times' for a positive ceiling", async () => {
      const fetchMock = showFlowFetchMock({
        entry: makeJsonResponse(200, {
          ...SHOW_DETAIL_WITH_OFFERABLE_SUGGESTION,
          card: JSON.stringify({
            schemaVersion: 1,
            name: "Morning Drive",
            tagline: "Wake up right",
            flavor: "Upbeat, punchy, keep it moving before 9am — never mellow, never slow.",
            envelope: { rotation: { maxPlays: 3, notAiredWithinDays: null } },
          }),
        }),
      });
      await openMorningDriveReview(fetchMock);

      expect(screen.getByText("Plays tracks aired at most 3 times")).toBeInTheDocument();
    });

    it("renders no rule line when the manifest carries no rotation opinion (a 1.0 manifest)", async () => {
      const fetchMock = showFlowFetchMock();
      await openMorningDriveReview(fetchMock);

      expect(screen.queryByText(/^Plays tracks/)).not.toBeInTheDocument();
    });
  });

  describe("Scenario: the soft hire offer", () => {
    it("offers 'also hire' when the suggested persona is on the shelf and not hired (SPEC F118.3)", async () => {
      const fetchMock = showFlowFetchMock();
      await completeMorningDriveImport(fetchMock);

      expect(await screen.findByText('"Morning Drive" imported.')).toBeInTheDocument();
      // Bare role (see openMorningDriveReview's own remarks on why a `{ name }` filter never
      // matches a dynamic Dialog.Title here) — only the offer dialog is open at this point.
      const offer = await screen.findByRole("dialog");
      expect(within(offer).getByText('Also hire "Flip"?')).toBeInTheDocument();
    });

    it("accepting the offer opens the persona's own full-card review — the existing hire flow, never a silent second import", async () => {
      const fetchMock = showFlowFetchMock();
      await completeMorningDriveImport(fetchMock);

      const offer = within(await screen.findByRole("dialog"));
      expect(offer.getByText('Also hire "Flip"?')).toBeInTheDocument();
      fireEvent.click(offer.getByRole("button", { name: "Review persona" }));

      // The SAME PersonaCardReviewModal a click on Flip's own shelf card + Hire would open — full
      // card, Confirm hire button, no request yet. The offer dialog is gone by the time this one
      // appears (a single, non-overlapping `open` dialog throughout — never both at once).
      const reviewDialog = within(await screen.findByRole("dialog"));
      expect(reviewDialog.getByText("Turns the record over")).toBeInTheDocument();
      expect(reviewDialog.getByRole("button", { name: "Confirm hire" })).toBeInTheDocument();
      expect(fetchMock.mock.calls.some(([url]) => String(url) === FLIP_IMPORT_URL)).toBe(false);
    });

    it("declining the offer imports the show and hires nothing", async () => {
      const fetchMock = showFlowFetchMock();
      await completeMorningDriveImport(fetchMock);

      const offer = within(await screen.findByRole("dialog"));
      expect(offer.getByText('Also hire "Flip"?')).toBeInTheDocument();
      const callsBeforeDecline = fetchMock.mock.calls.length;
      fireEvent.click(offer.getByRole("button", { name: "No thanks" }));

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      // Declining issues no request of any kind — the show import above already committed.
      expect(fetchMock.mock.calls.length).toBe(callsBeforeDecline);
      expect(fetchMock.mock.calls.some(([url]) => String(url) === FLIP_ENTRY_URL)).toBe(false);
      expect(fetchMock.mock.calls.some(([url]) => String(url) === FLIP_IMPORT_URL)).toBe(false);
    });

    it("renders no offer, and no error, when suggestedPersona is absent", async () => {
      const fetchMock = showFlowFetchMock({ entry: makeJsonResponse(200, SHOW_DETAIL_NO_SUGGESTION) });
      await completeMorningDriveImport(fetchMock);

      expect(await screen.findByText('"Morning Drive" imported.')).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    it("renders no offer, and no error, when suggestedPersona names a persona not on the shelf (unknown)", async () => {
      const fetchMock = showFlowFetchMock({ entry: makeJsonResponse(200, SHOW_DETAIL_UNKNOWN_SUGGESTION) });
      await completeMorningDriveImport(fetchMock, { entries: [SHOW_ENTRY] });

      expect(await screen.findByText('"Morning Drive" imported.')).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    it("renders no offer, and no error, when the suggested persona is already hired", async () => {
      const fetchMock = showFlowFetchMock();
      await completeMorningDriveImport(fetchMock, { hiredPersonaSlugs: ["flip"] });

      expect(await screen.findByText('"Morning Drive" imported.')).toBeInTheDocument();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });

    // PLAN T255 review finding F1 (HIGH): accepting the offer used to arm a bare `reviewing`
    // boolean before Flip's own entry fetch resolved, and nothing ever cleared it on failure — so
    // once that fetch failed, the review modal would auto-pop open the NEXT time ANY card's detail
    // finished loading, for a persona the operator never asked to review. The fix keys the arm to
    // the exact slug (`reviewingPersonaSlug`) and gates the modal's render on `detail.slug`
    // matching it — a failed fetch never reaches `detail.kind === "loaded"` at all, so it can never
    // satisfy that gate for ANY later card.
    it("F1 regression: a failed suggested-persona fetch never auto-opens the review modal for the next card clicked", async () => {
      const fetchMock = showFlowFetchMock({
        flipEntry: makeJsonResponse(404, { detail: 'No catalog entry with slug "flip" exists.' }),
      });
      const mixedEntries = [SHOW_ENTRY, FLIP_PERSONA_ENTRY, LENA_PERSONA_ENTRY];
      const view = await completeMorningDriveImport(fetchMock, { entries: mixedEntries });

      const offer = within(await screen.findByRole("dialog"));
      fireEvent.click(offer.getByRole("button", { name: "Review persona" }));

      // The failed fetch surfaces as the inline error text in Flip's own (still-open) detail
      // section — never a dialog of any kind. gh-#372 note: the shelf now silos kinds by tab and
      // this whole flow runs on the SHOWS tab, where Flip's persona section only renders through
      // the offer's own `reviewingPersonaSlug` exemption — this assertion now also pins that
      // exemption (without it, the failure would be silently invisible).
      await screen.findByText('No catalog entry with slug "flip" exists.');
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

      // gh-#372: Lena's card lives on the PERSONAS tab now — switch tabs the way production does,
      // a new `activeKind` prop on the SAME mounted client (state, including any stale offer arm,
      // survives exactly as it would live).
      view.rerender(
        <>
          <PersonaCatalogClient activeKind="persona"
            initialIndex={{ entries: mixedEntries, fetchedAt: "2026-08-10T00:00:00Z", unreachable: false }}
            importedShowSlugs={[]}
            hiredPersonaSlugs={[]}
          />
          <Toaster />
        </>
      );

      // Clicking a COMPLETELY different card, whose own fetch succeeds, must never pop the
      // stale, offer-armed review modal open for it (the exact bug: `reviewing` stayed `true`
      // forever once armed, so the NEXT successful load of ANY kind satisfied its render gate).
      // `getByRole("heading", ...)` (not `findByText`, which would also match the shelf card's
      // OWN `<span>` of the same name before the fetch even resolves): this targets ONLY
      // `DetailPanel`'s `<h2>`, proving Lena's own detail genuinely finished loading, not just that
      // her card exists on the shelf.
      fireEvent.click(cardFor("Late Night Lena"));
      await screen.findByRole("heading", { name: "Late Night Lena" });
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
  });

  describe("Scenario: installed-state honesty (the font/theme gh-#375 precedent, applied to shows)", () => {
    it("renders an Imported chip on the shelf card and 'Confirm re-import' in the modal when the slug already exists locally", async () => {
      const fetchMock = showFlowFetchMock();
      global.fetch = fetchMock;

      render(
        <PersonaCatalogClient activeKind="show"
          initialIndex={{ entries: [SHOW_ENTRY], fetchedAt: "2026-08-10T00:00:00Z", unreachable: false }}
          importedShowSlugs={["morning-drive"]}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).getByText("Imported")).toBeInTheDocument();

      fireEvent.click(cardFor("Morning Drive"));
      const dialog = within(await screen.findByRole("dialog"));
      await screen.findByText("Wake up right");
      expect(dialog.getByText("Imported")).toBeInTheDocument();
      expect(dialog.getByRole("button", { name: "Confirm re-import" })).toBeInTheDocument();
      expect(dialog.queryByRole("button", { name: "Confirm import" })).not.toBeInTheDocument();
    });

    // PLAN T255 review finding F2 (MEDIUM): `importedShowSlugs` used to carry EVERY `GET
    // /api/shows` row's slug unconditionally, including an AUTHORED row that merely collides with
    // a catalog entry's own slug (SPEC F115.5's two-provenance-class rule) — that authored slug
    // then lied with an "Imported" chip and a "Confirm re-import" button that would always 409.
    // The fix (`page.tsx`'s `fetchImportedShowSlugs`) now filters to `importedFrom !== null`
    // before this prop is ever built, so an authored-colliding slug simply never appears in it —
    // this test passes `importedShowSlugs={[]}` to stand in for that already-filtered shape (the
    // same "prop already reflects the fix" convention `theme-catalog-preview-install.spec.tsx`'s
    // own `installedThemeProvenance` fixtures use) and pins the CONSEQUENCE: no chip, an honest
    // "Confirm import" label, and — the chosen shape for "the 409-will-happen state" (review
    // finding F2's own open call) — the server's real 409 surfaces gracefully inside the
    // still-open modal via the SAME generic error path every other refused import already uses,
    // rather than a new, bespoke pre-emptive disable this client has no reliable way to predict
    // (the atomic upsert's own collision gate is the only place that decision can be made safely).
    it("an authored-colliding slug renders no Imported chip, and a refused re-import surfaces the server's 409 inside the still-open modal", async () => {
      const fetchMock = showFlowFetchMock({
        importResponse: makeJsonResponse(409, {
          detail: '"morning-drive" is an authored show\'s slug and cannot be overwritten by an import (SPEC F115.5).',
        }),
      });
      global.fetch = fetchMock;

      render(
        <PersonaCatalogClient activeKind="show"
          initialIndex={{ entries: [SHOW_ENTRY], fetchedAt: "2026-08-10T00:00:00Z", unreachable: false }}
          importedShowSlugs={[]}
        />
      );

      const grid = screen.getByRole("list", { name: "Community catalog entries" });
      expect(within(grid).queryByText("Imported")).not.toBeInTheDocument();

      fireEvent.click(cardFor("Morning Drive"));
      const dialog = within(await screen.findByRole("dialog"));
      await screen.findByText("Wake up right");
      expect(dialog.queryByText("Imported")).not.toBeInTheDocument();
      expect(dialog.getByRole("button", { name: "Confirm import" })).toBeInTheDocument();

      await act(async () => {
        fireEvent.click(dialog.getByRole("button", { name: "Confirm import" }));
        await Promise.resolve();
      });

      expect(await screen.findByRole("alert")).toHaveTextContent(
        '"morning-drive" is an authored show\'s slug and cannot be overwritten by an import (SPEC F115.5).'
      );
      // The dialog stays open — a refused confirm is not a crash, and the operator can still cancel.
      expect(screen.getByRole("dialog")).toBeInTheDocument();
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: cancelling reviews nothing", () => {
    it("makes no import request and imports no show when the owner cancels", async () => {
      const fetchMock = showFlowFetchMock();
      await openMorningDriveReview(fetchMock);

      const callsBeforeCancel = fetchMock.mock.calls.length;
      fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
      expect(fetchMock.mock.calls.length).toBe(callsBeforeCancel);
      expect(fetchMock.mock.calls.some(([url]) => String(url) === IMPORT_URL)).toBe(false);
    });
  });

  describe("Scenario: an unreachable entry degrades gracefully", () => {
    it("shows visible copy instead of crashing when the catalog entry is unreachable", async () => {
      const fetchMock = showFlowFetchMock({
        entry: makeJsonResponse(200, {
          card: null,
          meta: null,
          fetchedAt: null,
          unreachable: true,
          audience: null,
          bestFor: null,
          author: null,
          description: null,
          samplePatter: null,
          fontFamily: null,
          fontByteTotal: null,
          fontSpecimenFile: null,
          fontLicense: null,
          fontVersion: null,
          fontSubset: null,
          suggestedPersona: null,
        }),
      });
      global.fetch = fetchMock;

      render(
        <PersonaCatalogClient activeKind="show"
          initialIndex={{ entries: [SHOW_ENTRY], fetchedAt: "2026-08-10T00:00:00Z", unreachable: false }}
        />
      );
      fireEvent.click(cardFor("Morning Drive"));

      expect(await screen.findByText("Catalog unreachable — try again shortly.")).toBeInTheDocument();
    });
  });
});
