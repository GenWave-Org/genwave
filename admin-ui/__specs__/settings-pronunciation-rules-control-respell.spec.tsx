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
// T145-review-flagged tighter edit row is deferred, not built here. There is no availability-probe
// endpoint (T278 didn't ship one — PLAN T279 ruling): the assist is present until the FIRST attempt
// proves it absent with a 501, at which point it hides itself for the rest of this mount
// (attempt-and-hide, no new endpoint). The respelling itself is a scratch field: it is never part
// of a saved rule and never reaches POST /api/pronunciations's own body.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom";
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
      const mockFetch = makeFetchMock(getRows(), { status: 200, body: { ipa: "/məˈklaʊd/" } });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect({ url: requestUrl(mockFetch, 1), method: requestMethod(mockFetch, 1) }).toEqual({
        url: "/api/pronunciations/derive",
        method: "POST",
      });
    });

    it("posts the typed respelling as the request body", async () => {
      const mockFetch = makeFetchMock(getRows(), { status: 200, body: { ipa: "/məˈklaʊd/" } });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect(requestBody(mockFetch, 1)).toEqual({ respelling: "muh-KLOWD" });
    });

    it("writes the returned ipa into the IPA field on 200", async () => {
      makeFetchMock(getRows(), { status: 200, body: { ipa: "/məˈklaʊd/" } });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect(screen.getByLabelText("IPA")).toHaveValue("/məˈklaʊd/");
    });
  });

  describe("Scenario (sad path): the respelling is rejected", () => {
    it("surfaces the 400's message in place, next to the respelling field", async () => {
      makeFetchMock(getRows(), {
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
      makeFetchMock(getRows(), {
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
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(4));

      expect(screen.queryByText("respelling must not exceed 200 characters.")).not.toBeInTheDocument();
    });
  });

  describe("Scenario (sad path): espeak-ng is absent from this image (501)", () => {
    it("hides the Get IPA button", async () => {
      makeFetchMock(getRows(), { status: 501, body: {} });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect(screen.queryByRole("button", { name: /get ipa/i })).not.toBeInTheDocument();
    });

    it("hides the respelling input itself, not just the button", async () => {
      makeFetchMock(getRows(), { status: 501, body: {} });
      renderControl();
      await waitForLoaded();
      fillRespelling("muh-KLOWD");

      await clickGetIpa();

      expect(screen.queryByLabelText(/respelling/i)).not.toBeInTheDocument();
    });

    it("stays hidden through later interaction — the 501 latches for the rest of this mount", async () => {
      makeFetchMock(getRows(), { status: 501, body: {} });
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
      const mockFetch = makeFetchMock(getRows(), { status: 201, body: {} }, getRows());
      renderControl();
      await waitForLoaded();
      fireEvent.change(screen.getByLabelText("Pattern"), { target: { value: "Big Sur" } });
      fireEvent.change(screen.getByLabelText("Word (optional)"), { target: { value: "Sur" } });
      fireEvent.change(screen.getByLabelText("IPA"), { target: { value: "/sɜːr/" } });
      fillRespelling("big-SIR");

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: /add pronunciation/i }));
      });

      expect(requestBody(mockFetch, 1)).toEqual({ pattern: "Big Sur", word: "Sur", ipa: "/sɜːr/" });
    });
  });
});
