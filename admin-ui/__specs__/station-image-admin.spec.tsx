// STORY-339 — The station's own image: the authed admin tab-icon half (PLAN T307).
// Runner: Jest. Backend halves live in tests/GenWave.Host.Tests/Specs/Story339_TheStationsOwnImage.cs.
//
// The tab-icon swap is decided server-side inside the authed shell's own `generateMetadata` export
// (a Next.js metadata function, not a rendered component) — mirrors station-wordmark.spec.tsx's own
// "call the async server function directly, inspect what it returns" idiom (Server Components/
// metadata functions can't run under RTL). `generateMetadata` returning `icons` REPLACES (never
// merges with) the root layout's own file-convention `icons.png` for every page under this segment
// — SPEC F131.3's own "authenticated admin pages swap their tab icon" posture, proven here by
// asserting the returned `Metadata` object directly.
//
// PLAN T307 fix round (F1 blocker): `generateMetadata` no longer probes the bytes-carrying
// `GET /api/station/image` route per navigation purely for a 200-vs-404 status — it reads
// `stationImageToken` off the SAME `GET /api/stations` snapshot the shell's own wordmark already
// fetches (layout.tsx's `fetchStationSnapshot`), a bytes-free `IStationImageStore.GetTokenAsync`
// read on the backend. The resolved href now also carries that token as a `?v=` query param (rider
// R2, the PersonaFace `?v=` precedent) — cache-busting a re-upload's new bytes under a stable route.
//
// next/jest's SWC transform doesn't hoist jest.mock() above import statements (mirrors
// station-wordmark.spec.tsx's own header comment), so the mocked next/headers binding is never
// statically imported — layout.tsx is loaded via dynamic `await import()` inside each test.

jest.mock("next/headers", () => ({
  cookies: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import type { cookies } from "next/headers";

const mockedCookies = jest
  .requireMock<{ cookies: typeof cookies }>("next/headers")
  .cookies as jest.MockedFunction<typeof cookies>;

// ---------------------------------------------------------------------------
// Cookie store fake (mirrors station-wordmark.spec.tsx)
// ---------------------------------------------------------------------------

interface FakeCookieStore {
  get: (name: string) => { value: string } | undefined;
  toString: () => string;
}

function mockCookieStore(store: FakeCookieStore): void {
  mockedCookies.mockResolvedValue(store as unknown as Awaited<ReturnType<typeof cookies>>);
}

/** `.toString()` is pinned to this exact literal below (F2, PLAN T307 fix round) — the cookie-forward
 * fact asserts against `"genwave-auth=test-session"` directly rather than re-deriving it from this
 * same store instance, so a future edit here (e.g. `cookies: ""`) reds that fact instead of agreeing
 * with itself. */
function authedCookieStore(): FakeCookieStore {
  return { get: () => ({ value: "test-session" }), toString: () => "genwave-auth=test-session" };
}

// ---------------------------------------------------------------------------
// Fetch mock for GET /api/stations — the session snapshot `generateMetadata` now reads
// `stationImageToken` off (PLAN T307 fix round), never a per-navigation bytes probe.
// ---------------------------------------------------------------------------

function makeStationsFetchMock(
  stationImageToken: string | null,
  status = 200
): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: jest
      .fn<() => Promise<unknown>>()
      .mockResolvedValue([{ id: 1, name: "GenWave", stationImageToken }]),
    headers: new Headers(),
  } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function makeRejectingFetchMock(): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockRejectedValue(new Error("network down"));
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

describe("Feature: the admin console wears the station image", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockCookieStore(authedCookieStore());
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: the authed layout swaps the tab icon", () => {
    it("sets the favicon link to the token-versioned station image href once the snapshot reports one", async () => {
      makeStationsFetchMock("img-token-1");

      const { generateMetadata } = await import("../app/(authed)/layout");
      const metadata = await generateMetadata();

      expect(metadata.icons).toEqual({ icon: "/api/station/image?v=img-token-1" });
    });

    it("keeps the shipped icon when no station image is set", async () => {
      makeStationsFetchMock(null);

      const { generateMetadata } = await import("../app/(authed)/layout");
      const metadata = await generateMetadata();

      expect(metadata.icons).toBeUndefined();
    });
  });

  describe("Scenario (sad path): the fetch fails", () => {
    it("also keeps the shipped icon on a network error", async () => {
      makeRejectingFetchMock();

      const { generateMetadata } = await import("../app/(authed)/layout");
      const metadata = await generateMetadata();

      expect(metadata.icons).toBeUndefined();
    });

    it("also keeps the shipped icon on a non-200 snapshot response", async () => {
      makeStationsFetchMock(null, 500);

      const { generateMetadata } = await import("../app/(authed)/layout");
      const metadata = await generateMetadata();

      expect(metadata.icons).toBeUndefined();
    });
  });

  describe("Scenario: the cookie forwards to the backend (PLAN T307 fix round F2)", () => {
    it("forwards the authed session cookie to GET /api/stations", async () => {
      const fn = makeStationsFetchMock("img-token-1");

      const { generateMetadata } = await import("../app/(authed)/layout");
      await generateMetadata();

      const options = fn.mock.calls[0]?.[1] as { headers?: Record<string, string> } | undefined;
      expect(options?.headers?.["cookie"]).toBe("genwave-auth=test-session");
    });
  });
});
