// lib/broadcast-api.ts — shared now-playing/status/play-history fetchers
// (SPEC F28.6–F28.8), consumed by both the Dashboard (Q5) and Live (Q6) via
// lib/use-poll.ts.
//
// Runner: Jest (node environment — .ts extension). Mirrors the direct
// module-under-test style of catalog-f3-filter.spec.ts rather than driving
// a component: these are pure fetch wrappers with no rendering surface.
//
// Migration note: the deleted legacy live-page.spec.tsx (STORY-045) asserted
// `cache: "no-store"` on the now-playing/play-history poll fetches; that
// coverage was dropped when live-page.spec.tsx was folded into
// live-on-air-view.spec.tsx (Q6) and is restored here at the fetcher level —
// the single place all three pollers (now-playing, status, play-history)
// actually set the fetch options — rather than re-asserted per call site in
// every component spec that happens to poll through them.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { fetchNowPlaying, fetchPlayHistory, fetchStatus } from "../lib/broadcast-api";

function makeFetchMock(body: unknown, status = 200): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  } as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

describe("Feature: broadcast-api fetch options", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: every poller opts out of the HTTP cache and sends session credentials", () => {
    it("fetchNowPlaying calls fetch with cache: no-store and credentials: include", async () => {
      const mockFetch = makeFetchMock({ stationId: "1", mediaId: "m1", gainDb: 0, startedAt: "2026-01-01T00:00:00Z" });

      await fetchNowPlaying();

      expect(mockFetch).toHaveBeenCalledWith(
        "/api/now-playing",
        expect.objectContaining({ cache: "no-store", credentials: "include" })
      );
    });

    it("fetchStatus calls fetch with cache: no-store and credentials: include", async () => {
      const mockFetch = makeFetchMock({
        startedAt: "2026-01-01T00:00:00Z",
        catalog: { ready: 0, enriching: 0, failed: 0, unavailable: 0 },
        safeScope: { libraryIds: [], playable: 0 },
      });

      await fetchStatus();

      expect(mockFetch).toHaveBeenCalledWith(
        "/api/status",
        expect.objectContaining({ cache: "no-store", credentials: "include" })
      );
    });

    it("fetchPlayHistory calls fetch with cache: no-store and credentials: include", async () => {
      const mockFetch = makeFetchMock([]);

      await fetchPlayHistory();

      expect(mockFetch).toHaveBeenCalledWith(
        "/api/play-history",
        expect.objectContaining({ cache: "no-store", credentials: "include" })
      );
    });
  });
});
