// STORY-361 — The Announcements page (SPEC F146 · PLAN T344)
//
// BDD specification — jest. Drives AnnouncementsClient (send/history/token orchestrator) and
// AnnouncementHistoryList (the F143.2 visible-decline surface) via @testing-library/react with a
// mocked fetch — mirrors safe-content-page.spec.tsx in style. The page consumes ONLY the shipped
// endpoint family (POST/GET /api/announcements, GET/POST/DELETE /api/announcements/token[/status]) —
// no parallel write path.

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import { render, screen, fireEvent, act, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { AnnouncementsClient } from "../app/(authed)/announcements/AnnouncementsClient";
import type { AnnouncementsClientProps } from "../app/(authed)/announcements/AnnouncementsClient";
import { AnnouncementHistoryList } from "../app/(authed)/announcements/AnnouncementHistoryList";
import type { AnnouncementHistoryDto } from "@/lib/announcements-api";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderClient(overrides: Partial<AnnouncementsClientProps> = {}): ReturnType<typeof render> {
  const props: AnnouncementsClientProps = {
    initialSpectatorMode: false,
    initialHistory: [],
    initialTokenStatus: { hasToken: false, lastUsedAt: null },
    timeZone: "UTC",
    ...overrides,
  };
  return render(
    <ConfirmDialogProvider>
      <AnnouncementsClient {...props} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function makeHistoryEntry(overrides: Partial<AnnouncementHistoryDto> = {}): AnnouncementHistoryDto {
  return {
    id: 1,
    message: "Test message",
    verbatim: false,
    state: "pending",
    declineReason: null,
    collapseCount: 1,
    createdAt: "2026-08-22T10:00:00Z",
    expiresAt: "2026-08-22T10:15:00Z",
    airedAt: null,
    ...overrides,
  };
}

interface MockResponseSpec {
  status: number;
  body?: unknown;
}

/** A fetch mock that replays one response per call, in order (last spec repeats if exhausted) —
 * mirrors safe-content-page.spec.tsx's own `makeSequencedFetchMock` idiom. */
function makeSequencedFetchMock(specs: MockResponseSpec[]): jest.MockedFunction<typeof fetch> {
  let callIndex = 0;
  const fn = jest.fn<typeof fetch>().mockImplementation(async () => {
    const spec = specs[callIndex] ?? specs[specs.length - 1]!;
    callIndex += 1;
    return {
      ok: spec.status >= 200 && spec.status < 300,
      status: spec.status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
    } as unknown as Response;
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

// ---------------------------------------------------------------------------
// Feature: The Announcements page
// ---------------------------------------------------------------------------

describe("Feature: The Announcements page", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: sending from the page", () => {
    it("posts the typed message with the verbatim toggle through the one announcements endpoint (T344, STORY-361 AC1)", async () => {
      const mockFetch = makeSequencedFetchMock([
        { status: 200, body: { id: 7 } }, // POST /api/announcements
        { status: 200, body: [] }, // GET /api/announcements (post-send refresh)
      ]);
      renderClient();

      fireEvent.change(screen.getByLabelText("Message"), { target: { value: "Dinner is ready" } });
      fireEvent.click(screen.getByLabelText(/speak it exactly as written/i));

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Send" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(mockFetch).toHaveBeenCalled();
      });

      const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("/api/announcements");
      expect(init.method).toBe("POST");
      const body = JSON.parse(init.body as string) as Record<string, unknown>;
      expect(body["message"]).toBe("Dinner is ready");
      expect(body["verbatim"]).toBe(true);
    });

    it("shows the new entry immediately as pending (T344, STORY-361 AC1)", async () => {
      makeSequencedFetchMock([
        { status: 200, body: { id: 7 } }, // POST /api/announcements
        {
          status: 200,
          body: [makeHistoryEntry({ id: 7, message: "Dinner is ready", state: "pending" })],
        }, // GET /api/announcements (post-send refresh)
      ]);
      renderClient();

      fireEvent.change(screen.getByLabelText("Message"), { target: { value: "Dinner is ready" } });

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Send" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("Dinner is ready")).toBeInTheDocument();
        expect(screen.getByText("pending")).toBeInTheDocument();
      });
    });
  });

  describe("Scenario: the history is the visible-decline surface", () => {
    it("renders every reachable state with its decline reason where present (T344, STORY-361 AC2)", () => {
      render(
        <AnnouncementHistoryList
          timeZone="UTC"
          entries={[
            makeHistoryEntry({ id: 1, state: "pending" }),
            makeHistoryEntry({ id: 2, state: "claimed" }),
            makeHistoryEntry({ id: 3, state: "aired", airedAt: "2026-08-22T10:05:00Z" }),
            makeHistoryEntry({ id: 4, state: "expired" }),
            makeHistoryEntry({ id: 5, state: "declined", declineReason: "station went public" }),
          ]}
        />
      );

      for (const state of ["pending", "claimed", "aired", "expired", "declined"]) {
        expect(screen.getByText(state)).toBeInTheDocument();
      }
      expect(screen.getByText("station went public")).toBeInTheDocument();
    });

    it("renders collapse counts and aired timestamps (T344, STORY-361 AC2)", () => {
      render(
        <AnnouncementHistoryList
          timeZone="UTC"
          entries={[
            makeHistoryEntry({ id: 1, collapseCount: 4 }),
            makeHistoryEntry({ id: 2, state: "aired", airedAt: "2026-08-22T10:05:00Z" }),
          ]}
        />
      );

      expect(screen.getByText("×4")).toBeInTheDocument();
      expect(screen.getByText(/10:05/)).toBeInTheDocument();
    });
  });

  describe("Scenario: token management lives here", () => {
    it("reveals a generated token exactly once and shows last-used (T344, STORY-361 AC3)", async () => {
      makeSequencedFetchMock([
        { status: 200, body: { token: "plaintext-token-value" } }, // POST /api/announcements/token
        { status: 200, body: { hasToken: true, lastUsedAt: null } }, // GET status refresh
      ]);
      const { unmount } = renderClient({ initialTokenStatus: { hasToken: false, lastUsedAt: null } });

      expect(screen.queryByText("plaintext-token-value")).not.toBeInTheDocument();

      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Generate" }));
        await Promise.resolve();
      });

      await waitFor(() => {
        expect(screen.getByText("plaintext-token-value")).toBeInTheDocument();
      });

      // Navigating away (unmount) and back (a fresh render fed only by the server's own status
      // read, never a persisted plaintext) proves reveal-once: the plaintext never survives past
      // this one render, and the last-used indicator reflects the freshly-fetched status.
      unmount();
      renderClient({ initialTokenStatus: { hasToken: true, lastUsedAt: "2026-08-22T09:30:00Z" } });

      expect(screen.queryByText("plaintext-token-value")).not.toBeInTheDocument();
      expect(screen.getByText(/09:30/)).toBeInTheDocument();
    });
  });

  describe("Scenario: public mode says so", () => {
    it("replaces the send with an explanation while SpectatorMode is on (T344, STORY-361 AC4)", () => {
      renderClient({ initialSpectatorMode: true });

      expect(screen.queryByLabelText("Message")).not.toBeInTheDocument();
      expect(screen.getByText(/station is public/i)).toBeInTheDocument();
    });
  });
});
