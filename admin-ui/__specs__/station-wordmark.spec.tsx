// @jest-environment jsdom
// STORY-138 — Station identity goes live (Epic V / SPEC F44.7, closes gitea-#195) — wordmark half.
// The API/provider half lives in Host.Tests/Specs/Story138_StationIdentityLive.cs.
//
// Runner: Jest (jsdom) + @testing-library/react. Authored PENDING at /plan time (2026-07-14,
// house rule since Epic S) as it.todo entries — V9 implements against Sidebar, MobileNav, and
// the login page.
//
// The station name is fetched exactly once, server-side, in the authed shell's layout.tsx (an
// async server component) and threaded down to Sidebar/MobileNav as a `stationName` prop — so
// the fetch/fallback behavior lives entirely in the layout, mirroring how settings-server.spec.ts
// and app-shell.spec.tsx call an async server component directly as a plain function and inspect
// its returned element tree, rather than rendering it through RTL (Server Components can't run
// under RTL). Sidebar/MobileNav/LoginPage are exercised the normal RTL way since they're the
// client (or client-adjacent) surfaces that actually paint the wordmark.
//
// next/jest's SWC transform doesn't hoist jest.mock() above import statements (mirrors
// app-shell.spec.tsx's header comment), so the mocked next/navigation/next/headers bindings are
// never statically imported — Sidebar/MobileNav/layout/login-page are all loaded via dynamic
// `await import()` inside each test.

jest.mock("next/navigation", () => ({
  usePathname: jest.fn(),
  redirect: jest.fn(),
}));

jest.mock("next/headers", () => ({
  cookies: jest.fn(),
}));

jest.mock("@/app/login/actions", () => ({
  logout: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom";
import type { ReactNode } from "react";
import type { cookies } from "next/headers";
import type { usePathname } from "next/navigation";

const mockedCookies = jest
  .requireMock<{ cookies: typeof cookies }>("next/headers")
  .cookies as jest.MockedFunction<typeof cookies>;
const mockedUsePathname = jest
  .requireMock<{ usePathname: typeof usePathname }>("next/navigation")
  .usePathname as jest.MockedFunction<typeof usePathname>;

// ---------------------------------------------------------------------------
// Cookie store fakes (mirrors app-shell.spec.tsx)
// ---------------------------------------------------------------------------

interface FakeCookieStore {
  get: (name: string) => { value: string } | undefined;
  toString: () => string;
}

function mockCookieStore(store: FakeCookieStore): void {
  mockedCookies.mockResolvedValue(store as unknown as Awaited<ReturnType<typeof cookies>>);
}

function noSessionCookieStore(): FakeCookieStore {
  return { get: () => undefined, toString: () => "" };
}

function authedCookieStore(): FakeCookieStore {
  return { get: () => ({ value: "test-session" }), toString: () => "genwave-auth=test-session" };
}

// ---------------------------------------------------------------------------
// Fetch mock for GET /api/stations
// ---------------------------------------------------------------------------

function makeStationsFetchMock(
  stations: Array<{ id: number; name: string }>,
  status = 200
): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(stations),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function makeRejectingFetchMock(): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockRejectedValue(new Error("network down"));
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

// ---------------------------------------------------------------------------
// Tree walkers over a server component's returned element (mirror
// settings-server.spec.ts / app-shell.spec.tsx — these never render through
// RTL, they inspect the plain element tree returned by calling the async
// function directly).
// ---------------------------------------------------------------------------

function collectStrings(node: ReactNode, out: string[] = []): string[] {
  if (node === null || node === undefined || typeof node === "boolean") {
    return out;
  }
  if (typeof node === "string" || typeof node === "number") {
    out.push(String(node));
    return out;
  }
  if (Array.isArray(node)) {
    for (const child of node) collectStrings(child, out);
    return out;
  }
  const el = node as { props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props && el.props["children"] !== undefined) {
    collectStrings(el.props["children"] as ReactNode, out);
  }
  return out;
}

/** Finds the first element of the given component type anywhere in a server-component element tree. */
function findElementByType(node: ReactNode, type: unknown): { props: Record<string, unknown> } | undefined {
  if (node === null || node === undefined || typeof node !== "object") {
    return undefined;
  }
  if (Array.isArray(node)) {
    for (const child of node) {
      const found = findElementByType(child, type);
      if (found) return found;
    }
    return undefined;
  }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el.type === type) {
    return el as { props: Record<string, unknown> };
  }
  if (el.props && el.props["children"] !== undefined) {
    return findElementByType(el.props["children"] as ReactNode, type);
  }
  return undefined;
}

// ---------------------------------------------------------------------------
// Feature: The shell wears the station name
// ---------------------------------------------------------------------------

describe("Feature: The shell wears the station name", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    mockedUsePathname.mockReturnValue("/dashboard");
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: authenticated surfaces show the station", () => {
    it("renders the station name as the Sidebar wordmark, server-fetched (F44.7)", async () => {
      mockCookieStore(authedCookieStore());
      makeStationsFetchMock([{ id: 1, name: "WKRP Radio" }]);

      const { default: AuthedLayout } = await import("../app/(authed)/layout");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      const tree = await AuthedLayout({ children: "content" });
      const sidebarEl = findElementByType(tree, Sidebar);
      const stationName = sidebarEl?.props["stationName"] as string;

      render(<Sidebar stationName={stationName} />);

      expect(screen.getByText("WKRP Radio")).toBeInTheDocument();
    });

    it("renders the station name in the MobileNav drawer header (F44.7)", async () => {
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");

      render(<MobileNav stationName="WKRP Radio" />);
      fireEvent.click(screen.getByRole("button", { name: "Open navigation" }));

      expect(await screen.findByText("WKRP Radio")).toBeInTheDocument();
    });

    it("keeps the Fraunces display treatment on both wordmarks (F28.3 unchanged)", async () => {
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");

      const { unmount } = render(<Sidebar stationName="WKRP Radio" />);
      const sidebarWordmark = screen.getByText("WKRP Radio");
      expect(sidebarWordmark.className).toMatch(/font-display/);
      expect(sidebarWordmark.className).toMatch(/italic/);
      unmount(); // avoid ambiguous "WKRP Radio" matches once MobileNav also mounts one

      render(<MobileNav stationName="WKRP Radio" />);
      fireEvent.click(screen.getByRole("button", { name: "Open navigation" }));
      const drawerWordmark = await screen.findByText("WKRP Radio");
      expect(drawerWordmark.className).toMatch(/font-display/);
      expect(drawerWordmark.className).toMatch(/italic/);
    });
  });

  describe("Scenario: the product brand stays where it belongs", () => {
    it("keeps 'GenWave' on the login page — you log into GenWave, then operate your station (F44.7)", async () => {
      mockCookieStore(noSessionCookieStore());

      const { default: LoginPage } = await import("../app/login/page");
      const node = await LoginPage({ searchParams: Promise.resolve({}) });

      expect(collectStrings(node)).toContain("GenWave");
    });
  });

  describe("Scenario (sad path): the fetch fails", () => {
    it("falls back to 'GenWave' when the station-name fetch fails (F44.7)", async () => {
      mockCookieStore(authedCookieStore());
      makeStationsFetchMock([], 500); // non-200

      const { default: AuthedLayout } = await import("../app/(authed)/layout");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      const tree = await AuthedLayout({ children: "content" });
      const sidebarEl = findElementByType(tree, Sidebar);

      expect(sidebarEl?.props["stationName"]).toBe("GenWave");
    });

    it("also falls back to 'GenWave' on a network error and on an empty station list", async () => {
      const { default: AuthedLayout } = await import("../app/(authed)/layout");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      mockCookieStore(authedCookieStore());
      makeRejectingFetchMock();
      const networkErrorTree = await AuthedLayout({ children: "content" });
      expect(findElementByType(networkErrorTree, Sidebar)?.props["stationName"]).toBe("GenWave");

      mockCookieStore(authedCookieStore());
      makeStationsFetchMock([]); // 200 OK, empty list
      const emptyListTree = await AuthedLayout({ children: "content" });
      expect(findElementByType(emptyListTree, Sidebar)?.props["stationName"]).toBe("GenWave");
    });
  });
});
