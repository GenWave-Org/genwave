// @jest-environment jsdom
// gh-#… / SPEC F126.2 / STORY-324 / PLAN T279 — the add form's respell→IPA assist ("Get IPA").
//
// A sibling of settings-pronunciation-rules-control.spec.tsx and
// settings-pronunciation-rules-control-audition.spec.tsx, for the same reason those two are
// already split apart: a distinct feature with its own network shape (POST
// /api/pronunciations/derive, no blob()), kept in its own small file rather than folded into an
// already-reviewed suite.
//
// "Get IPA" posts { respelling } to POST /api/pronunciations/derive and writes the returned `ipa`
// straight into the add form's own IPA field — the operator then adjusts/auditions (T275's "Hear
// it") before ever saving. Add-form only (PLAN T279 ruling): the primary authoring surface; the
// T145-review-flagged tighter edit row is deferred, not built here. The respelling itself is a
// scratch field: it is never part of a saved rule and never reaches POST /api/pronunciations's own
// body.
//
// gh-#487: the control now also probes GET /api/pronunciations/derive/available once on mount, off
// the SAME server-side IRespellOracle.IsAvailable latch the 501 path reads, so an espeak-less image
// hides the assist BEFORE the operator's first click rather than after one dead-end 501 (the old
// PLAN T279 "no availability probe" ruling this supersedes). Every scenario below now stubs that
// mount-time GET alongside the initial GET /api/pronunciations list load; the "Feature: the mount
// capability probe" block at the end of this file covers the probe's own true/false/network-fail
// outcomes directly. The 501 attempt-and-hide fallback stays covered above unchanged — it is still
// reachable whenever the probe itself couldn't catch an absent binary.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { PronunciationRulesControl } from "../app/(authed)/settings/PronunciationRulesControl";

// ---------------------------------------------------------------------------
// Fetch mock — an ordered queue (the T145/T275 suites' own idiom).
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

/** The initial `GET /api/pronunciations` every render triggers — an empty list unless overridden,
 * since this suite's own scenarios don't need any saved rows. */
function getRows(rows: unknown[] = []): MockResponseSpec {
  return { status: 200, body: rows };
}

/** The mount-time `GET /api/pronunciations/derive/available` probe (gh-#487) every render also
 * now triggers, alongside {@link getRows} — `available: true` unless a scenario is specifically
 * exercising the probe's own false/absent behavior, so every OTHER scenario in this file (written
 * before the probe existed) keeps seeing the assist exactly as it did before. */
function probeAvailable(available = true): MockResponseSpec {
  return { status: 200, body: { available } };
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

function fillRespelling(value: string): void {
  fireEvent.change(screen.getByLabelText(/respelling/i), { target: { value } });
}

async function clickGetIpa(): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /get ipa/i }));
    await Promise.resolve();
  });
}

// ---------------------------------------------------------------------------
// Shared setup
// ---------------------------------------------------------------------------

let originalFetch: typeof fetch;

beforeEach(() => {
  originalFetch = global.fetch;
});

afterEach(() => {
  global.fetch = originalFetch;
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: deriving candidate IPA from a respelling
// ---------------------------------------------------------------------------

describe("Feature: the add form's respell assist (SPEC F126.2, STORY-324)", () => {
  describe("Scenario: the operator asks to derive IPA from a respelling", () => {
    it("posts to POST /api/pronunciations/derive", async () => {
      const mockFetch = makeFetchMock(getRows(), probeAvailable(), { status: 200, body: { ipa: "/məˈklaʊd/" } });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect({ url: requestUrl(mockFetch, 2), method: requestMethod(mockFetch, 2) }).toEqual({
        url: "/api/pronunciations/derive",
        method: "POST",
      });
    });

    it("posts the typed respelling as the request body", async () => {
      const mockFetch = makeFetchMock(getRows(), probeAvailable(), { status: 200, body: { ipa: "/məˈklaʊd/" } });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect(requestBody(mockFetch, 2)).toEqual({ respelling: "muh-KLOWD" });
    });

    it("writes the returned ipa into the IPA field on 200", async () => {
      makeFetchMock(getRows(), probeAvailable(), { status: 200, body: { ipa: "/məˈklaʊd/" } });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect(screen.getByLabelText("IPA")).toHaveValue("/məˈklaʊd/");
    });
  });

  describe("Scenario (sad path): the respelling is rejected", () => {
    it("surfaces the 400's message in place, next to the respelling field", async () => {
      makeFetchMock(getRows(), probeAvailable(), {
        status: 400,
        body: { detail: "respelling must not exceed 200 characters." },
      });
      renderControl();
      await waitForLoaded();
      fillRespelling("way too long".repeat(20));

      await clickGetIpa();

      expect(await screen.findByText("respelling must not exceed 200 characters.")).toBeInTheDocument();
    });

    it("does not toast the rejection — it is an inline field message, not a mutation-outcome toast", async () => {
      makeFetchMock(getRows(), probeAvailable(), {
        status: 400,
        body: { detail: "respelling must not exceed 200 characters." },
      });
      renderControl();
      await waitForLoaded();
      fillRespelling("way too long".repeat(20));

      await clickGetIpa();
      await screen.findByText("respelling must not exceed 200 characters.");

      expect(screen.queryByRole("status")).not.toBeInTheDocument();
    });

    it("clears the stale rejection message once the operator fixes the IPA by hand and adds the rule", async () => {
      const mockFetch = makeFetchMock(
        getRows(),
        probeAvailable(),
        { status: 400, body: { detail: "respelling must not exceed 200 characters." } },
        { status: 201, body: {} },
        getRows()
      );
      renderControl();
      await waitForLoaded();
      fillRespelling("way too long".repeat(20));
      await clickGetIpa();
      await screen.findByText("respelling must not exceed 200 characters.");

      fireEvent.change(screen.getByLabelText("Pattern"), { target: { value: "Big Sur" } });
      fireEvent.change(screen.getByLabelText("IPA"), { target: { value: "/sɜːr/" } });
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /add pronunciation/i }));
      });
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(5));

      expect(screen.queryByText("respelling must not exceed 200 characters.")).not.toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): espeak-ng is absent from this image (501)", () => {
    it("hides the Get IPA button", async () => {
      makeFetchMock(getRows(), probeAvailable(), { status: 501, body: {} });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect(screen.queryByRole("button", { name: /get ipa/i })).not.toBeInTheDocument();
    });

    it("hides the respelling input itself, not just the button", async () => {
      makeFetchMock(getRows(), probeAvailable(), { status: 501, body: {} });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect(screen.queryByLabelText(/respelling/i)).not.toBeInTheDocument();
    });

    it("stays hidden through later interaction — the 501 latches for the rest of this mount", async () => {
      makeFetchMock(getRows(), probeAvailable(), { status: 501, body: {} });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");
      await clickGetIpa();

      fireEvent.change(screen.getByLabelText("Pattern"), { target: { value: "Big Sur" } });

      expect(screen.queryByLabelText(/respelling/i)).not.toBeInTheDocument();
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: the respelling is a scratch field — it is never persisted
// ---------------------------------------------------------------------------

describe("Feature: the respelling never reaches a saved rule (STORY-324 ruling)", () => {
  describe("Scenario: the operator types a respelling, then saves the rule by hand", () => {
    it("never includes the respelling in POST /api/pronunciations's own body", async () => {
      const mockFetch = makeFetchMock(getRows(), probeAvailable(), { status: 201, body: {} }, getRows());
      renderControl();
      await waitForLoaded();
      fireEvent.change(screen.getByLabelText("Pattern"), { target: { value: "Big Sur" } });
      fireEvent.change(screen.getByLabelText("Word (optional)"), { target: { value: "Sur" } });
      fireEvent.change(screen.getByLabelText("IPA"), { target: { value: "/sɜːr/" } });
      fillRespelling("big-SIR");

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /add pronunciation/i }));
      });

      expect(requestBody(mockFetch, 2)).toEqual({ pattern: "Big Sur", word: "Sur", ipa: "/sɜːr/" });
    });
  });
});

// ---------------------------------------------------------------------------
// Feature: the mount capability probe (gh-#487)
// ---------------------------------------------------------------------------

describe("Feature: the mount capability probe (gh-#487)", () => {
  describe("Scenario: the probe reports the assist unavailable", () => {
    it("never renders the Get IPA button", async () => {
      makeFetchMock(getRows(), probeAvailable(false));
      renderControl();
      await waitForLoaded();

      await waitFor(() => expect(screen.queryByRole("button", { name: /get ipa/i })).not.toBeInTheDocument());
    });

    it("never renders the respelling input either", async () => {
      makeFetchMock(getRows(), probeAvailable(false));
      renderControl();
      await waitForLoaded();

      await waitFor(() => expect(screen.queryByLabelText(/respelling/i)).not.toBeInTheDocument());
    });

    it("never fires a derive call, since there is nothing left to click", async () => {
      const mockFetch = makeFetchMock(getRows(), probeAvailable(false));
      renderControl();
      await waitForLoaded();
      await waitFor(() => expect(screen.queryByRole("button", { name: /get ipa/i })).not.toBeInTheDocument());

      // Exactly the two mount-time calls (rows + probe) — no third call, since there is no button
      // left for a click to reach POST /api/pronunciations/derive through.
      expect(mockFetch).toHaveBeenCalledTimes(2);
    });
  });

  describe("Scenario: the probe reports the assist available", () => {
    it("renders the Get IPA button, same as before this probe existed", async () => {
      makeFetchMock(getRows(), probeAvailable(true));
      renderControl();
      await waitForLoaded();

      expect(await screen.findByRole("button", { name: /get ipa/i })).toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): the probe itself fails over the network", () => {
    it("still renders the assist — a transient probe failure assumes available, not hidden", async () => {
      const fn = jest
        .fn<typeof fetch>()
        .mockResolvedValueOnce(toResponse(getRows()))
        .mockRejectedValueOnce(new Error("network down"));
      global.fetch = fn as unknown as typeof fetch;
      renderControl();
      await waitForLoaded();

      expect(await screen.findByRole("button", { name: /get ipa/i })).toBeInTheDocument();
    });
  });
});
