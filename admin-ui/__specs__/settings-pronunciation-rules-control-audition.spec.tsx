// @jest-environment jsdom
// gh-#… / SPEC F126.1 / STORY-323 / PLAN T275 — the rules editor's "Hear it" audition button.
//
// A sibling of settings-pronunciation-rules-control.spec.tsx (T145's own suite) rather than an
// addition to it: this file's own fetch mock needs a `blob()` implementation on top of `json()`
// (the personas-page.spec.tsx idiom for the same POST /api/tts/preview endpoint), and its own
// URL.createObjectURL/revokeObjectURL stubs (jsdom ships neither) — folding those into T145's
// existing ordered-queue harness would touch a large, already-reviewed file for a feature that
// suite never anticipated.
//
// "Hear it" posts { text, candidateRules: [{pattern, word, ipa}] } to POST /api/tts/preview and
// plays the returned wav via a blob URL — the exact idiom PersonaPreview already established for
// this same endpoint. Station rows only (never persona rows, which are read-only/card-owned here);
// both a saved row and the add-form's own in-progress draft can audition.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor, within } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
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
    pattern: "Reykjavík",
    word: "Reykjavík",
    ipa: "/ˈreɪkjaviːk/",
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
  ipa: "/məˈklaʊd/",
  source: "persona",
  hitCount: 5,
});

// ---------------------------------------------------------------------------
// Fetch mock — an ordered queue (the T145 suite's own idiom), extended with `blob()` (the
// personas-page.spec.tsx idiom for this same endpoint).
// ---------------------------------------------------------------------------

interface MockResponseSpec {
  status: number;
  body?: unknown;
  blob?: Blob;
}

function toResponse(spec: MockResponseSpec): Response {
  return {
    ok: spec.status >= 200 && spec.status < 300,
    status: spec.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
    blob: jest.fn<() => Promise<Blob>>().mockResolvedValue(spec.blob ?? new Blob(["wav-bytes"], { type: "audio/wav" })),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

function makeFetchMock(...specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  const queue = [...specs];
  const fn = jest.fn<typeof fetch>().mockImplementation(() => {
    const next = queue.shift();
    if (next === undefined) throw new Error("unexpected fetch call — no stubbed response left");
    return Promise.resolve(toResponse(next));
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function getRow(status: number, rows: RuleRowFixture[]): MockResponseSpec {
  return { status, body: rows };
}

/** The mount-time `GET /api/pronunciations/derive/available` probe (gh-#487) — every render now
 * fires this alongside the initial rows load, so every queue in this file stubs it too
 * (`available: true`; this suite's own scenarios never exercise the probe's absent/error paths —
 * that lives in settings-pronunciation-rules-control-respell.spec.tsx). */
function probeAvailable(): MockResponseSpec {
  return { status: 200, body: { available: true } };
}

function renderControl(): ReturnType<typeof render> {
  return render(
    <ConfirmDialogProvider>
      <PronunciationRulesControl />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

async function waitForLoaded(): Promise<void> {
  await waitFor(() => expect(screen.queryByText(/loading pronunciation rules/i)).not.toBeInTheDocument());
}

function dataRow(name: RegExp): HTMLElement {
  return screen.getByRole("row", { name });
}

function requestBody(mockFetch: jest.MockedFunction<typeof fetch>, callIndex: number): unknown {
  const call = mockFetch.mock.calls[callIndex] as unknown as [string, RequestInit?];
  return JSON.parse(String(call[1]?.body));
}

async function clickHearIt(within_: HTMLElement): Promise<void> {
  await act(async () => {
    fireEvent.click(within(within_).getByRole("button", { name: /hear it/i }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Shared setup
// ---------------------------------------------------------------------------

let originalFetch: typeof fetch;

beforeEach(() => {
  originalFetch = global.fetch;
  // jsdom ships no Blob-URL implementation at all — AuditionButton's playback path needs both
  // mocked so it can hand the <audio> element a src (the personas-page.spec.tsx idiom).
  URL.createObjectURL = jest.fn<(obj: Blob | MediaSource) => string>(() => "blob:mock-url");
  URL.revokeObjectURL = jest.fn<(url: string) => void>();
});

afterEach(() => {
  global.fetch = originalFetch;
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: auditioning a saved station row
// ---------------------------------------------------------------------------

describe("Feature: auditioning a station row (SPEC F126.1, STORY-323)", () => {
  describe("Scenario: the operator asks to hear the row's own rule", () => {
    it("posts the row's own pattern/word/ipa as the single candidate rule", async () => {
      const mockFetch = makeFetchMock(
        getRow(200, [STATION_ROW]),
        probeAvailable(),
        { status: 200, blob: new Blob(["wav"]) }
      );
      renderControl();
      await waitForLoaded();

      await clickHearIt(dataRow(/Reykjavík/));

      expect(requestBody(mockFetch, 2)).toMatchObject({
        candidateRules: [{ pattern: "Reykjavík", word: "Reykjavík", ipa: "/ˈreɪkjaviːk/" }],
      });
    });

    it("posts a phrase that contains the row's own pattern verbatim, so the rule actually fires", async () => {
      const mockFetch = makeFetchMock(
        getRow(200, [STATION_ROW]),
        probeAvailable(),
        { status: 200, blob: new Blob(["wav"]) }
      );
      renderControl();
      await waitForLoaded();

      await clickHearIt(dataRow(/Reykjavík/));

      const body = requestBody(mockFetch, 2) as { text: string };
      expect(body.text).toContain("Reykjavík");
    });

    it("posts to POST /api/tts/preview", async () => {
      const mockFetch = makeFetchMock(
        getRow(200, [STATION_ROW]),
        probeAvailable(),
        { status: 200, blob: new Blob(["wav"]) }
      );
      renderControl();
      await waitForLoaded();

      await clickHearIt(dataRow(/Reykjavík/));

      const call = mockFetch.mock.calls[2] as unknown as [string, RequestInit];
      expect(call[0]).toBe("/api/tts/preview");
      expect(call[1].method).toBe("POST");
    });

    it("shows a pending state while the request is in flight", async () => {
      let resolvePreview: (value: Response) => void = () => {};
      const previewPromise = new Promise<Response>((resolve) => {
        resolvePreview = resolve;
      });
      const fn = jest
        .fn<typeof fetch>()
        .mockResolvedValueOnce(toResponse(getRow(200, [STATION_ROW])))
        .mockResolvedValueOnce(toResponse(probeAvailable()))
        .mockImplementationOnce(() => previewPromise);
      global.fetch = fn as unknown as typeof fetch;
      renderControl();
      await waitForLoaded();

      const row = dataRow(/Reykjavík/);
      await act(async () => {
        fireEvent.click(within(row).getByRole("button", { name: /hear it/i }));
        await Promise.resolve();
      });

      // The accessible name stays "Hear it for …" throughout (the aria-label, not the visible text
      // — the same "Save"/"Saving…" split T145's own review F4 already established); the pending
      // state shows in the button's own text content and its disabled attribute instead.
      const button = within(row).getByRole("button", { name: /hear it/i });
      expect(button.textContent).toBe("Rendering…");
      expect(button).toBeDisabled();

      await act(async () => {
        resolvePreview(toResponse({ status: 200, blob: new Blob(["wav"]) }));
        await Promise.resolve();
      });
    });

    it("plays the returned audio on 200", async () => {
      makeFetchMock(
        getRow(200, [STATION_ROW]),
        probeAvailable(),
        { status: 200, blob: new Blob(["wav-bytes"], { type: "audio/wav" }) }
      );
      renderControl();
      await waitForLoaded();

      await clickHearIt(dataRow(/Reykjavík/));

      const audio = await within(dataRow(/Reykjavík/)).findByLabelText(/audition audio/i);
      expect(audio).toHaveAttribute("src", "blob:mock-url");
    });

    it("toasts the field-named message on a 400 — the real shape is a ValidationProblemDetails "
      + "with no `detail`, so the button reads its `errors` dict directly, same as the write path", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]), probeAvailable(), {
        status: 400,
        body: { errors: { "candidateRules[0].ipa": ["Ipa must not contain ')', '[', or ']'."] } },
      });
      renderControl();
      await waitForLoaded();

      await clickHearIt(dataRow(/Reykjavík/));

      expect(await screen.findByText("Ipa must not contain ')', '[', or ']'.")).toBeInTheDocument();
    });

    it("toasts a message on a 500", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]), probeAvailable(), { status: 500, body: {} });
      renderControl();
      await waitForLoaded();

      await clickHearIt(dataRow(/Reykjavík/));

      expect(await screen.findByText("Unexpected error (500)")).toBeInTheDocument();
    });

    it("never renders an <audio> element after a failed request", async () => {
      makeFetchMock(getRow(200, [STATION_ROW]), probeAvailable(), { status: 500, body: {} });
      renderControl();
      await waitForLoaded();

      await clickHearIt(dataRow(/Reykjavík/));
      await screen.findByText("Unexpected error (500)");

      expect(within(dataRow(/Reykjavík/)).queryByLabelText(/audition audio/i)).not.toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: a persona row is never auditionable — it isn't being authored here
// ---------------------------------------------------------------------------

describe("Feature: a persona row carries no audition affordance (STORY-323 ruling)", () => {
  describe("Scenario: the merged list includes a card-owned persona rule", () => {
    it("renders no Hear it button on the persona row", async () => {
      makeFetchMock(getRow(200, [PERSONA_ROW]), probeAvailable());
      renderControl();
      await waitForLoaded();

      expect(within(dataRow(/MacLeod/)).queryByRole("button", { name: /hear it/i })).not.toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: auditioning the add-form's own draft, before it is ever saved
// ---------------------------------------------------------------------------

describe("Feature: auditioning the add form's draft rule (STORY-323 core value)", () => {
  function fillDraft(pattern: string, word: string, ipa: string): void {
    fireEvent.change(screen.getByLabelText("Pattern"), { target: { value: pattern } });
    fireEvent.change(screen.getByLabelText("Word (optional)"), { target: { value: word } });
    fireEvent.change(screen.getByLabelText("IPA"), { target: { value: ipa } });
  }

  describe("Scenario: the operator has typed a pattern/ipa but not yet saved", () => {
    it("posts the typed draft as the candidate rule, never a saved row", async () => {
      const mockFetch = makeFetchMock(getRow(200, []), probeAvailable(), { status: 200, blob: new Blob(["wav"]) });
      renderControl();
      await waitForLoaded();
      fillDraft("Big Sur", "Sur", "/sɜːr/");

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /hear it/i }));
        await Promise.resolve();
      });

      expect(requestBody(mockFetch, 2)).toMatchObject({
        candidateRules: [{ pattern: "Big Sur", word: "Sur", ipa: "/sɜːr/" }],
      });
    });

    it("sends a blank draft word as-is, not defaulted to the pattern client-side — "
      + "PronunciationRuleResolver.ResolveForRender's own PronunciationRuleSet.Create→PronunciationRule.Parse "
      + "chain applies that default server-side, the same one the write path relies on", async () => {
      const mockFetch = makeFetchMock(getRow(200, []), probeAvailable(), { status: 200, blob: new Blob(["wav"]) });
      renderControl();
      await waitForLoaded();
      fillDraft("Big Sur", "", "/sɜːr/");

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /hear it/i }));
        await Promise.resolve();
      });

      const body = requestBody(mockFetch, 2) as { candidateRules: [{ word: string }] };
      expect(body.candidateRules[0].word).toBe("");
    });

    it("plays the returned audio on 200", async () => {
      makeFetchMock(
        getRow(200, []),
        probeAvailable(),
        { status: 200, blob: new Blob(["wav-bytes"], { type: "audio/wav" }) }
      );
      renderControl();
      await waitForLoaded();
      fillDraft("Big Sur", "Sur", "/sɜːr/");

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /hear it/i }));
        await Promise.resolve();
      });

      const audio = await screen.findByLabelText(/draft rule audition audio/i);
      expect(audio).toHaveAttribute("src", "blob:mock-url");
    });

    it("never POSTs the add draft to /api/pronunciations — auditioning never saves anything", async () => {
      const mockFetch = makeFetchMock(getRow(200, []), probeAvailable(), { status: 200, blob: new Blob(["wav"]) });
      renderControl();
      await waitForLoaded();
      fillDraft("Big Sur", "Sur", "/sɜːr/");

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /hear it/i }));
        await Promise.resolve();
      });

      const postsToPronunciations = mockFetch.mock.calls.some((call) => {
        const [url, init] = call as unknown as [string, RequestInit?];
        return String(url) === "/api/pronunciations" && init?.method === "POST";
      });
      expect(postsToPronunciations).toBe(false);
    });
  });

  describe("Scenario: the draft has no pattern or ipa yet", () => {
    it("disables the Hear it button until the draft could possibly compile", async () => {
      makeFetchMock(getRow(200, []), probeAvailable());
      renderControl();
      await waitForLoaded();

      expect(screen.getByRole("button", { name: /hear it/i })).toBeDisabled();
    });
  });
});
