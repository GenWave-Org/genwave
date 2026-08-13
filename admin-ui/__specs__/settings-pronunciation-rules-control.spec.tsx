// @jest-environment jsdom
// gh-#284 / STORY-254 / PLAN T145 — pronunciation rules render as editable rows, not a JSON blob.
//
// The API (T144, PronunciationsController) merges the station JSON array with the active
// persona's own rules: a row names its source ("station" | "persona"), whether it's the one
// actually firing (a station rule shares its identity with a persona rule → shadowed), its hit
// count (attached ONLY to the in-effect row — T142 review ruling), and, for a station rule that
// never compiled, the Reason it was dropped. PronunciationRulesControl reads/writes
// /api/pronunciations directly — never the Tts:Pronunciations settings blob — so every add/
// edit/delete resolves immediately against its own 201/400/409/404, rather than riding a
// page-wide Save.
//
// Runner: Jest (jsdom) + @testing-library/react. Fetch is mocked as an ordered queue (the
// settings-engine-by-kind-control.spec.tsx / catalog-purge-unavailable.spec.tsx idiom) since this
// component issues its GET at mount, then a fresh GET after every successful mutation.
// `makeFetchMock`'s queue is asserted DRAINED in a file-wide afterEach (PLAN T145 review F2): a
// stubbed response nothing ever consumed means the interaction under test never actually
// happened — the failure mode behind the original "never re-fetches on a rejected add" fact,
// which passed for the wrong reason (a disabled Add button, not a rejected POST).

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PronunciationRulesControl } from "../app/(authed)/settings/PronunciationRulesControl";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

interface RuleRowFixture {
  pattern: string;
  word: string;
  ipa: string;
  source: "station" | "persona";
  inEffect: boolean;
  hitCount: number | null;
  reason: string | null;
}

function makeRow(overrides: Partial<RuleRowFixture> = {}): RuleRowFixture {
  return {
    pattern: "Big Sur",
    word: "Sur",
    ipa: "/sɜːr/",
    source: "station",
    inEffect: true,
    hitCount: 3,
    reason: null,
    ...overrides,
  };
}

const STATION_ROW = makeRow();
const PERSONA_ROW = makeRow({
  pattern: "MacLeod",
  word: "MacLeod",
  ipa: "/cardIpa/",
  source: "persona",
  hitCount: 5,
});
const SHADOWED_ROW = makeRow({
  pattern: "Reykjavík",
  word: "Reykjavík",
  ipa: "/oldIpa/",
  inEffect: false,
  hitCount: null,
  reason: null,
});
const DEAD_ROW = makeRow({
  pattern: "",
  word: "",
  ipa: "",
  inEffect: false,
  hitCount: null,
  reason: "Ipa must not be blank after trimming slash delimiters and whitespace.",
});
const UNFIRED_ROW = makeRow({ pattern: "Wynd", word: "Wynd", ipa: "/wɪnd/", hitCount: null });

/** A shadowed pair (PLAN T145 review F1, the AC2 scenario): a station rule and a persona rule
 * sharing the SAME (pattern, word) identity — the persona wins, so the station row is shadowed.
 * Distinguished in queries by their own (different) ipa, never by pattern/word alone. */
const SHADOWED_STATION_MACLEOD = makeRow({
  pattern: "MacLeod",
  word: "MacLeod",
  ipa: "/stationIpa/",
  source: "station",
  inEffect: false,
  hitCount: null,
  reason: null,
});
const PERSONA_MACLEOD = makeRow({
  pattern: "MacLeod",
  word: "MacLeod",
  ipa: "/personaIpa/",
  source: "persona",
  inEffect: true,
  hitCount: 2,
  reason: null,
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface MockResponseSpec {
  status: number;
  body?: unknown;
}

function toResponse(spec: MockResponseSpec): Response {
  return {
    ok: spec.status >= 200 && spec.status < 300,
    status: spec.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

/** The queue behind whichever `makeFetchMock` is currently active — asserted drained in the
 * file-wide `afterEach` below (PLAN T145 review F2). `null` between tests and for any test that
 * mocks fetch its own way (the "list fails to load" scenarios) rather than through this helper. */
let pendingQueue: MockResponseSpec[] | null = null;

/** Queues one response per call, in order — fails loudly once exhausted so an unexpected extra
 * fetch (a stray refetch, a double-submit) is never silently absorbed by a repeated spec. */
function makeFetchMock(...specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  const queue = [...specs];
  pendingQueue = queue;
  const fn = jest.fn<typeof fetch>().mockImplementation(() => {
    const next = queue.shift();
    if (next === undefined) throw new Error("unexpected fetch call — no stubbed response left");
    return Promise.resolve(toResponse(next));
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

// File-wide: runs for EVERY test regardless of which `describe` block it lives in. A leftover
// stubbed response means the test's own action never reached the network call it was meant to —
// exactly the vacuous-fact failure mode PLAN T145 review F2 found.
afterEach(() => {
  if (pendingQueue !== null) {
    expect(pendingQueue).toHaveLength(0);
  }
  pendingQueue = null;
});

function renderControl(): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      <PronunciationRulesControl />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

/** Waits for the initial GET to resolve and the table to render. */
async function waitForLoaded(): Promise<void> {
  await waitFor(() => expect(screen.queryByText(/loading pronunciation rules/i)).not.toBeInTheDocument());
}

function requestUrl(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number): string {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit?];
  return call[0];
}

function requestMethod(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number): string | undefined {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit?];
  return call[1]?.method;
}

function requestBody(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number): unknown {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit?];
  return JSON.parse(String(call[1]?.body));
}

function dataRow(name: RegExp): HTMLElement {
  return screen.getByRole("row", { name });
}

/** Clicks a row's own Delete button — opens the confirm dialog (PLAN T145 review F5) without
 * confirming it. */
async function clickDeleteButton(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /delete/i }));
    await Promise.resolve();
  });
}

/** Full delete flow: click Delete, then confirm in the dialog — the only path that actually
 * fires the DELETE request (PLAN T145 review F5). */
async function deleteRow(): Promise<void> {
  await clickDeleteButton();
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Feature: the merged rows render, not a JSON blob
// ---------------------------------------------------------------------------

describe("Feature: pronunciation rules render as rows (STORY-254)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: an in-effect station row", () => {
    it("renders its pattern", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/Big Sur/)).getByText("Big Sur")).toBeInTheDocument();
    });

    it("renders its word", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/Big Sur/)).getByText("Sur")).toBeInTheDocument();
    });

    it("renders its ipa", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/Big Sur/)).getByText("/sɜːr/")).toBeInTheDocument();
    });

    it("renders a brass Station source chip", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/Big Sur/)).getByText("Station")).toBeInTheDocument();
    });

    it("renders its hit count", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/Big Sur/)).getByText("3")).toBeInTheDocument();
    });

    it("renders 0 for an in-effect rule that has never fired, rather than a dash", async () => {
      makeFetchMock(getRow(200, [UNFIRED_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/Wynd/)).getByText("0")).toBeInTheDocument();
    });

    it("names the rule's own pattern in the Edit button's aria-label, not a positional index (review F4)", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      expect(screen.getByRole("button", { name: 'Edit "Big Sur"' })).toBeInTheDocument();
    });
  });

  describe("Scenario: a persona row is card-owned", () => {
    it("renders the Persona source chip", async () => {
      makeFetchMock(getRow(200, [PERSONA_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/MacLeod/)).getByText("Persona")).toBeInTheDocument();
    });

    it("renders no Edit affordance", async () => {
      makeFetchMock(getRow(200, [PERSONA_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/MacLeod/)).queryByRole("button", { name: /edit/i })).not.toBeInTheDocument();
    });

    it("renders no Delete affordance", async () => {
      makeFetchMock(getRow(200, [PERSONA_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/MacLeod/)).queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
    });

    it("names the card as the edit path in the UI's own language", async () => {
      makeFetchMock(getRow(200, [PERSONA_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/MacLeod/)).getByText(/edit on the persona.s card/i)).toBeInTheDocument();
    });
  });

  describe("Scenario: a shadowed station row", () => {
    it("is visibly marked not in effect", async () => {
      makeFetchMock(getRow(200, [SHADOWED_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/Reykjavík/)).getByText(/not in effect/i)).toBeInTheDocument();
    });

    it("renders no hit count — a shadowed row carries none (T142 review ruling)", async () => {
      makeFetchMock(getRow(200, [SHADOWED_ROW]));
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/Reykjavík/)).getByText("—")).toBeInTheDocument();
    });
  });

  describe("Scenario: a station row that never compiled", () => {
    it("shows its dropped reason in place of the not-in-effect note", async () => {
      makeFetchMock(getRow(200, [DEAD_ROW]));
      renderControl();
      await waitForLoaded();

      expect(
        screen.getByText("Ipa must not be blank after trimming slash delimiters and whitespace.")
      ).toBeInTheDocument();
    });

    it("stays addressable — still deletable by its blank identity", async () => {
      const mockFetch = makeFetchMock(getRow(200, [DEAD_ROW]), { status: 204 }, getRow(200, []));
      renderControl();
      await waitForLoaded();

      await deleteRow();

      expect(requestUrl(mockFetch, 1)).toBe("/api/pronunciations?pattern=&word=");
    });
  });

  describe("Scenario: the list fails to load", () => {
    it("shows an error message", async () => {
      const fn = jest.fn<typeof fetch>().mockRejectedValue(new Error("network down"));
      global.fetch = fn as unknown as typeof fetch;
      renderControl();

      expect(await screen.findByText(/unable to load pronunciation rules/i)).toBeInTheDocument();
    });

    it("offers a Retry that re-fetches", async () => {
      const fn = jest
        .fn<typeof fetch>()
        .mockRejectedValueOnce(new Error("network down"))
        .mockResolvedValueOnce(toResponse(getRow(200, [STATION_ROW])));
      global.fetch = fn as unknown as typeof fetch;
      renderControl();
      await screen.findByText(/unable to load pronunciation rules/i);

      fireEvent.click(screen.getByRole("button", { name: /retry/i }));

      expect(await screen.findByText("Big Sur")).toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: adding a rule
// ---------------------------------------------------------------------------

describe("Feature: adding a pronunciation rule", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  async function fillAndSubmitAdd(pattern: string, word: string, ipa: string): Promise<void> {
    fireEvent.change(screen.getByLabelText("Pattern"), { target: { value: pattern } });
    fireEvent.change(screen.getByLabelText("Word (optional)"), { target: { value: word } });
    fireEvent.change(screen.getByLabelText("IPA"), { target: { value: ipa } });
    await act(async () => {
      // "Add pronunciation", not "Add rule" (T145 review round 2 note) — this tab already has
      // CorrectionsSettingControl's own "Add rule" button; the rename avoids two same-named
      // buttons in one tabpanel.
      fireEvent.click(screen.getByRole("button", { name: /add pronunciation/i }));
    });
  }

  describe("Scenario: the rule is accepted", () => {
    it("POSTs the trimmed pattern/word/ipa", async () => {
      const mockFetch = makeFetchMock(getRow(200, []), { status: 201, body: {} }, getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      await fillAndSubmitAdd("Big Sur", "Sur", "/sɜːr/");

      expect(requestBody(mockFetch, 1)).toEqual({ pattern: "Big Sur", word: "Sur", ipa: "/sɜːr/" });
    });

    it("refreshes the list with the new row", async () => {
      makeFetchMock(getRow(200, []), { status: 201, body: {} }, getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      await fillAndSubmitAdd("Big Sur", "Sur", "/sɜːr/");

      expect(await screen.findByText("Big Sur")).toBeInTheDocument();
    });

    it("toasts the outcome", async () => {
      makeFetchMock(getRow(200, []), { status: 201, body: {} }, getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      await fillAndSubmitAdd("Big Sur", "Sur", "/sɜːr/");

      expect(await screen.findByText("Pronunciation rule added.")).toBeInTheDocument();
    });

    it("sends a null word when the word field is left blank", async () => {
      const mockFetch = makeFetchMock(getRow(200, []), { status: 201, body: {} }, getRow(200, []));
      renderControl();
      await waitForLoaded();

      await fillAndSubmitAdd("MacLeod", "", "/x/");

      expect(requestBody(mockFetch, 1)).toEqual({ pattern: "MacLeod", word: null, ipa: "/x/" });
    });
  });

  describe("Scenario: the rule is accepted but collides with a speech correction (gh-#491)", () => {
    const COLLISION_WARNING =
      "A speech correction ('MacLeod' → 'Maa-cloud') targets the same word. Pronunciation rules " +
      "take precedence: that correction is suppressed on every render where this rule is in play. " +
      "Delete the correction if it is now redundant.";

    it("toasts the write's collision warning alongside the success toast", async () => {
      makeFetchMock(
        getRow(200, []),
        { status: 201, body: { rule: STATION_ROW, warnings: [COLLISION_WARNING] } },
        getRow(200, [STATION_ROW])
      );
      renderControl();
      await waitForLoaded();

      await fillAndSubmitAdd("MacLeod", "", "/x/");

      expect(await screen.findByText("Pronunciation rule added.")).toBeInTheDocument();
      expect(await screen.findByText(/suppressed on every render/)).toBeInTheDocument();
    });

    it("toasts nothing extra when the write carries no warnings", async () => {
      makeFetchMock(
        getRow(200, []),
        { status: 201, body: { rule: STATION_ROW, warnings: [] } },
        getRow(200, [STATION_ROW])
      );
      renderControl();
      await waitForLoaded();

      await fillAndSubmitAdd("Big Sur", "Sur", "/sɜːr/");

      expect(await screen.findByText("Pronunciation rule added.")).toBeInTheDocument();
      expect(screen.queryByText(/suppressed on every render/)).not.toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): the rule is rejected in place", () => {
    it("surfaces the 400's field message under the offending field, not as a toast", async () => {
      makeFetchMock(getRow(200, []), {
        status: 400,
        body: { errors: { ipa: ["Ipa must not contain ')', '[', or ']'."] } },
      });
      renderControl();
      await waitForLoaded();

      await fillAndSubmitAdd("MacLeod", "", "/x)/");

      expect(screen.getByText("Ipa must not contain ')', '[', or ']'.")).toBeInTheDocument();
    });

    it("never re-fetches the list on a rejected add (review F2: a real invalid rule, not a client-blocked one)", async () => {
      const mockFetch = makeFetchMock(getRow(200, []), {
        status: 400,
        body: { errors: { ipa: ["Ipa must not contain ')', '[', or ']'."] } },
      });
      renderControl();
      await waitForLoaded();

      // Pattern and ipa are both non-blank — the Add button stays enabled and the POST actually
      // fires; the server, not the client, is what rejects this malformed ipa.
      await fillAndSubmitAdd("MacLeod", "", "/x)/");

      expect(mockFetch).toHaveBeenCalledTimes(2);
    });
  });

  describe("Scenario (sad path): the identity collides with an existing station rule", () => {
    it("shows the 409's detail as a row-level message, not a toast", async () => {
      makeFetchMock(getRow(200, []), {
        status: 409,
        body: { detail: "An existing station rule already matches pattern 'MacLeod' word 'MacLeod'." },
      });
      renderControl();
      await waitForLoaded();

      await fillAndSubmitAdd("MacLeod", "", "/y/");

      expect(
        screen.getByText("An existing station rule already matches pattern 'MacLeod' word 'MacLeod'.")
      ).toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: editing a rule in place
// ---------------------------------------------------------------------------

describe("Feature: editing a pronunciation rule in place", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the edit is accepted", () => {
    it("PUTs to the original identity's query-string address", async () => {
      const mockFetch = makeFetchMock(
        getRow(200, [STATION_ROW]),
        { status: 200, body: {} },
        getRow(200, [STATION_ROW])
      );
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));
      fireEvent.change(screen.getByLabelText(/ipa for/i), { target: { value: "/newIpa/" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
      });

      expect(requestUrl(mockFetch, 1)).toBe("/api/pronunciations?pattern=Big%20Sur&word=Sur");
    });

    it("PUTs with the edited body", async () => {
      const mockFetch = makeFetchMock(
        getRow(200, [STATION_ROW]),
        { status: 200, body: {} },
        getRow(200, [STATION_ROW])
      );
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));
      fireEvent.change(screen.getByLabelText(/ipa for/i), { target: { value: "/newIpa/" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
      });

      expect(requestBody(mockFetch, 1)).toEqual({ pattern: "Big Sur", word: "Sur", ipa: "/newIpa/" });
    });

    it("returns to Edit/Delete once saved", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]), { status: 200, body: {} }, getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
      });

      expect(await screen.findByRole("button", { name: /edit/i })).toBeInTheDocument();
    });

    it("shows plain 'Save' as the button's visible text — identity lives only in aria-label (review F4)", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));

      expect(screen.getByRole("button", { name: /save/i }).textContent).toBe("Save");
    });
  });

  describe("Scenario: a shadowed pair shares one (pattern, word) identity (review F1, AC2)", () => {
    it("editing the station row leaves the persona row read-only, still showing its own ipa", async () => {
      makeFetchMock(getRow(200, [SHADOWED_STATION_MACLEOD, PERSONA_MACLEOD]));
      renderControl();
      await waitForLoaded();

      const stationRow = dataRow(/stationIpa/);
      await act(async () => {
        fireEvent.click(within(stationRow).getByRole("button", { name: /edit/i }));
      });

      // If the persona row were (mis)matched as being edited too, its ipa would render as an
      // <input value="/personaIpa/"> instead of plain text — getByText finds only the latter.
      expect(within(dataRow(/personaIpa/)).getByText("/personaIpa/")).toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): the edited rule is rejected in place", () => {
    it("surfaces the field message inline on the row, still editing", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]), {
        status: 400,
        body: { errors: { ipa: ["Ipa must not be blank after trimming slash delimiters and whitespace."] } },
      });
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));
      fireEvent.change(screen.getByLabelText(/ipa for/i), { target: { value: "" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
      });

      expect(
        screen.getByText("Ipa must not be blank after trimming slash delimiters and whitespace.")
      ).toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): the new identity collides with a different rule", () => {
    it("shows the collision as a row-level message", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]), {
        status: 409,
        body: { detail: "An existing station rule already matches pattern 'Alpha' word 'Alpha'." },
      });
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));
      fireEvent.change(screen.getByLabelText(/pattern for/i), { target: { value: "Alpha" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
      });

      expect(
        screen.getByText("An existing station rule already matches pattern 'Alpha' word 'Alpha'.")
      ).toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): the target row went stale", () => {
    it("toasts the outcome", async () => {
      makeFetchMock(
        getRow(200, [STATION_ROW]),
        {
          status: 404,
          body: { detail: "No station pronunciation rule matches pattern 'Big Sur' word 'Sur'." },
        },
        getRow(200, [])
      );
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
      });

      expect(
        await screen.findByText(/this rule no longer exists.*refreshed/i)
      ).toBeInTheDocument();
    });

    it("refreshes the list", async () => {
      const mockFetch = makeFetchMock(
        getRow(200, [STATION_ROW]),
        {
          status: 404,
          body: { detail: "No station pronunciation rule matches pattern 'Big Sur' word 'Sur'." },
        },
        getRow(200, [])
      );
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
      });

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3));
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: deleting a rule
// ---------------------------------------------------------------------------

describe("Feature: deleting a pronunciation rule", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the rule is removed", () => {
    it("DELETEs the row's own identity", async () => {
      const mockFetch = makeFetchMock(getRow(200, [STATION_ROW]), { status: 204 }, getRow(200, []));
      renderControl();
      await waitForLoaded();

      await deleteRow();

      expect(requestUrl(mockFetch, 1)).toBe("/api/pronunciations?pattern=Big%20Sur&word=Sur");
    });

    it("toasts the outcome", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]), { status: 204 }, getRow(200, []));
      renderControl();
      await waitForLoaded();

      await deleteRow();

      expect(await screen.findByText("Pronunciation rule removed.")).toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): the row was already deleted elsewhere", () => {
    it("toasts the outcome", async () => {
      makeFetchMock(
        getRow(200, [STATION_ROW]),
        { status: 404, body: { detail: "No station pronunciation rule matches pattern 'Big Sur' word 'Sur'." } },
        getRow(200, [])
      );
      renderControl();
      await waitForLoaded();

      await deleteRow();

      expect(await screen.findByText(/this rule no longer exists/i)).toBeInTheDocument();
    });

    it("refreshes the list", async () => {
      const mockFetch = makeFetchMock(
        getRow(200, [STATION_ROW]),
        { status: 404, body: { detail: "No station pronunciation rule matches pattern 'Big Sur' word 'Sur'." } },
        getRow(200, [])
      );
      renderControl();
      await waitForLoaded();

      await deleteRow();

      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(3));
    });
  });

  describe("Scenario: delete requires confirmation (review F5)", () => {
    it("opens a confirm dialog naming the consequence before any DELETE fires", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      await clickDeleteButton();

      expect(
        await screen.findByText('Delete the pronunciation rule for "Big Sur"? This cannot be undone.')
      ).toBeInTheDocument();
    });

    it("cancelling the dialog leaves the rule intact — no DELETE fires", async () => {
      const mockFetch = makeFetchMock(getRow(200, [STATION_ROW]));
      renderControl();
      await waitForLoaded();

      await clickDeleteButton();
      const dialog = await screen.findByRole("dialog");
      await act(async () => {
        fireEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));
        await Promise.resolve();
      });

      expect(mockFetch).toHaveBeenCalledTimes(1);
    });
  });

  describe("Scenario: the Delete button guards against a double click (review F5)", () => {
    it("disables Delete while the request is in flight, so a second click cannot fire twice", async () => {
      let resolveDelete: (value: Response) => void = () => {};
      const deletePromise = new Promise<Response>((resolve) => {
        resolveDelete = resolve;
      });
      const fn = jest
        .fn<typeof fetch>()
        .mockResolvedValueOnce(toResponse(getRow(200, [STATION_ROW])))
        .mockImplementationOnce(() => deletePromise)
        .mockResolvedValueOnce(toResponse(getRow(200, [])));
      global.fetch = fn as unknown as typeof fetch;
      renderControl();
      await waitForLoaded();

      await deleteRow();

      expect(screen.getByRole("button", { name: /delete/i })).toBeDisabled();

      await act(async () => {
        resolveDelete(toResponse({ status: 204 }));
        await Promise.resolve();
      });
      await waitFor(() => expect(fn).toHaveBeenCalledTimes(3));
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: URL-hostile patterns/words are encoded on every content-addressed call
// ---------------------------------------------------------------------------

describe("Feature: content-addressed PUT/DELETE encode URL-hostile pattern/word text", () => {
  let originalFetch: typeof fetch;
  const HOSTILE_ROW = makeRow({ pattern: "Rock & Roll / R&B", word: "R&B", ipa: "/ɑːrˈændˈbiː/" });

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: editing a row whose identity carries spaces, ampersands, and slashes", () => {
    it("percent-encodes the query string on PUT", async () => {
      const mockFetch = makeFetchMock(
        getRow(200, [HOSTILE_ROW]),
        { status: 200, body: {} },
        getRow(200, [HOSTILE_ROW])
      );
      renderControl();
      await waitForLoaded();

      fireEvent.click(screen.getByRole("button", { name: /edit/i }));
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /save/i }));
      });

      expect(requestUrl(mockFetch, 1)).toBe(
        `/api/pronunciations?pattern=${encodeURIComponent("Rock & Roll / R&B")}&word=${encodeURIComponent("R&B")}`
      );
    });
  });

  describe("Scenario: deleting a row whose identity carries spaces, ampersands, and slashes", () => {
    it("percent-encodes the query string on DELETE", async () => {
      const mockFetch = makeFetchMock(getRow(200, [HOSTILE_ROW]), { status: 204 }, getRow(200, []));
      renderControl();
      await waitForLoaded();

      await deleteRow();

      expect(requestUrl(mockFetch, 1)).toBe(
        `/api/pronunciations?pattern=${encodeURIComponent("Rock & Roll / R&B")}&word=${encodeURIComponent("R&B")}`
      );
    });

    it("uses DELETE as the method", async () => {
      const mockFetch = makeFetchMock(getRow(200, [HOSTILE_ROW]), { status: 204 }, getRow(200, []));
      renderControl();
      await waitForLoaded();

      await deleteRow();

      expect(requestMethod(mockFetch, 1)).toBe("DELETE");
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: the add UI is never a <form> (T145 review round 3 — the nested-form browser defect)
// ---------------------------------------------------------------------------
//
// This control is mounted inside SettingsForm's own page-wide <form> (via ttsTabExtra). A <form>
// nested inside another <form> is invalid HTML: real browsers silently STRIP the inner <form>
// element entirely, so an onSubmit handler on it never binds, and a type="submit" button inside
// it instead submits the OUTER SettingsForm natively — observed live against the running stack as
// a full navigation to bare /settings with no POST to /api/pronunciations ever sent, no rule ever
// created. jsdom tolerates the nesting and happily fires a nested <form>'s own onSubmit, which is
// exactly why the rest of this suite passed 43/43 while the real browser did not. This guard is
// the one jsdom CAN see: if the Add control is ever rebuilt with a <form> wrapper again, this
// fails immediately instead of waiting for the next live/Playwright pass to notice. Do not "fix"
// this back to a <form> — see PronunciationRulesControl's own render-time comment for the reason.
describe("Feature: the add UI is never a <form> (nested-form browser defect)", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the control renders inside another page's own <form>", () => {
    it("mounts no <form> element of its own", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]));
      const { container } = renderControl();
      await waitForLoaded();

      expect(container.querySelector("form")).toBeNull();
    });
  });
});

// ---------------------------------------------------------------------------
// Local helper — declared last so every scenario above reads top-to-bottom before its definition
// ---------------------------------------------------------------------------

function getRow(status: number, rows: RuleRowFixture[]): MockResponseSpec {
  return { status, body: rows };
}
