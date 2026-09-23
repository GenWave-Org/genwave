// STORY-475 — A stale cookie sends you to sign in (gh-#729 · SPEC F208 · PLAN T566)
//
// Runner: Jest (node environment — .ts extension, catalog-pages.spec.ts's own convention).
// Node, not jsdom, is deliberate: this suite exercises real `NextRequest`/`NextResponse`
// instances (the middleware + session-expired route-handler scenarios) — next/server's classes
// extend the platform `Request`/`Response`, which Node provides natively but
// jest-environment-jsdom's realm does not (jestjs/jest#13037). The login page's server-rendered
// output is inspected via the same recursive tree-walker catalog-pages.spec.ts/
// settings-server.spec.ts use instead of RTL's render/screen — no DOM is needed to read string
// leaves out of a returned React element tree.
//
// Every specification below was RED (it.todo) at plan time; T566 turns each into a real `it`.
//
// next/jest's SWC transform (unlike babel-jest) does not hoist jest.mock() calls above import
// statements — ES import declarations are always evaluated first regardless of source position
// (mirrors app-shell.spec.tsx / settings-server.spec.ts's own header comments) — so
// app/login/page.tsx (which transitively calls next/headers' cookies()) is loaded via a dynamic
// `await import()` inside each "the login page" Given-describe below, after the mock has already
// registered. middleware.ts and app/session-expired/route.ts touch neither next/headers nor
// next/navigation, so both are safe to import statically.

jest.mock("next/headers", () => ({
  cookies: jest.fn(),
}));

import { describe, it, expect, jest, beforeEach, beforeAll } from "@jest/globals";
import type { ReactNode } from "react";
import { readFileSync, readdirSync, existsSync } from "node:fs";
import path from "node:path";
import { NextRequest } from "next/server";
import type { NextResponse } from "next/server";
import type { cookies } from "next/headers";
import { apiFetch } from "@/lib/api-fetch";
import type { NavigateFn } from "@/lib/api-fetch";
import { middleware } from "../middleware";
import { GET as sessionExpiredGet } from "../app/session-expired/route";

const mockedCookies = jest
  .requireMock<{ cookies: typeof cookies }>("next/headers")
  .cookies as jest.MockedFunction<typeof cookies>;

// ---------------------------------------------------------------------------
// Tree walker (mirrors catalog-pages.spec.ts / settings-server.spec.ts)
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
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props) {
    if (el.props["children"] !== undefined) {
      collectStrings(el.props["children"] as ReactNode, out);
    }
  }
  return out;
}

function treeContains(node: ReactNode, text: string): boolean {
  return collectStrings(node).some((s) => s.includes(text));
}

// ---------------------------------------------------------------------------
// Other helpers
// ---------------------------------------------------------------------------

/** A duck-typed fetch Response (this suite's `global.fetch` mocks follow the same shape as
 * every other __specs__ fetch mock in this directory — a plain object, not a real `Response`
 * instance — station-thumb-controls.spec.tsx's `installBoothLogFetchMock` is the house
 * precedent). */
function jsonResponse(status: number, body: unknown = {}): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: () => Promise.resolve(body),
  } as unknown as Response;
}

/** Drains the microtask queue enough for apiFetch's internal `await fetch(...)` (the request
 * itself) and, on a 401, its second `await fetch(...)` (the logout POST) to both settle before a
 * test inspects the mocks apiFetch drove. No real waiting/timers — apiFetch's 401 promise never
 * settles (ruling #3), so there is nothing to `await` on the call itself. */
async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 10; i += 1) {
    await Promise.resolve();
  }
}

// ---------------------------------------------------------------------------
// Feature: A stale cookie sends you to sign in
// ---------------------------------------------------------------------------

describe("Feature: A stale cookie sends you to sign in", () => {
  describe("Scenario: a 401 through apiFetch", () => {
    let fetchMock: jest.MockedFunction<typeof fetch>;
    let navigate: jest.MockedFunction<NavigateFn>;

    // Given: fetch mocked 401 (then 200 for the follow-up logout POST), navigate injected
    beforeEach(async () => {
      fetchMock = jest
        .fn<typeof fetch>()
        .mockResolvedValueOnce(jsonResponse(401))
        .mockResolvedValueOnce(jsonResponse(200));
      global.fetch = fetchMock as unknown as typeof fetch;
      navigate = jest.fn<NavigateFn>();

      void apiFetch("/api/media/1/vote", { method: "POST", navigate });
      await flushMicrotasks();
    });

    it("AC1 — the session cookie is cleared", () => {
      expect(fetchMock.mock.calls[1]).toEqual([
        "/api/auth/logout",
        expect.objectContaining({ method: "POST" }),
      ]);
    });

    it("AC2 — the router navigates to /login?expired=1", () => {
      expect(navigate).toHaveBeenCalledWith("/login?expired=1");
    });
  });

  describe("Scenario: the login page with the flag", () => {
    describe("Given expired=1 in the search params", () => {
      let node: ReactNode;

      beforeAll(async () => {
        mockedCookies.mockResolvedValue(
          { get: () => undefined } as unknown as Awaited<ReturnType<typeof cookies>>
        );
        const { default: LoginPage } = await import("../app/login/page");
        node = await LoginPage({ searchParams: Promise.resolve({ expired: "1" }) });
      });

      it('AC3 — shows "Your session ended. Sign in again."', () => {
        expect(treeContains(node, "Your session ended. Sign in again.")).toBe(true);
      });
    });

    describe("Given no expired flag in the search params", () => {
      let node: ReactNode;

      beforeAll(async () => {
        mockedCookies.mockResolvedValue(
          { get: () => undefined } as unknown as Awaited<ReturnType<typeof cookies>>
        );
        const { default: LoginPage } = await import("../app/login/page");
        node = await LoginPage({ searchParams: Promise.resolve({}) });
      });

      it("the flag's text is absent", () => {
        expect(treeContains(node, "Your session ended. Sign in again.")).toBe(false);
      });
    });
  });

  describe("Scenario: the former 401 sites", () => {
    const ADMIN_UI_ROOT = path.resolve(__dirname, "..");
    const SCAN_DIRS = ["app", "lib", "components"];
    const SKIP_DIRS = new Set(["node_modules", ".next", "__specs__"]);
    // Catches `=== 401`, `== 401`, `!== 401`, `!= 401`, `case 401`, and the reversed forms
    // (`401 === x`, `401 == x`, `401 !== x`, `401 != x`) — any comparison shape a former 401
    // branch could have been written in (review finding, T566 round 1).
    const STATUS_401_PATTERN =
      /(?:={2,3}|!={1,2})\s*401\b|\b401\s*(?:={2,3}|!={1,2})|case\s+401\b/;

    function collectSourceFiles(dir: string, out: string[]): void {
      for (const entry of readdirSync(dir, { withFileTypes: true })) {
        if (SKIP_DIRS.has(entry.name)) continue;
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          collectSourceFiles(full, out);
        } else if (entry.name.endsWith(".ts") || entry.name.endsWith(".tsx")) {
          out.push(full);
        }
      }
    }

    let scannedFiles: string[];
    let offenders: string[];

    // Given: admin-ui source scanned (app/, lib/, components/, middleware.ts)
    beforeAll(() => {
      scannedFiles = [];
      for (const dir of SCAN_DIRS) {
        const full = path.join(ADMIN_UI_ROOT, dir);
        if (existsSync(full)) collectSourceFiles(full, scannedFiles);
      }
      const middlewarePath = path.join(ADMIN_UI_ROOT, "middleware.ts");
      if (existsSync(middlewarePath)) scannedFiles.push(middlewarePath);

      offenders = scannedFiles
        .filter((file) => STATUS_401_PATTERN.test(readFileSync(file, "utf-8")))
        .map((file) => path.relative(ADMIN_UI_ROOT, file))
        .sort();
    });

    it("AC4 — no `status === 401` branch outside api-fetch.ts", () => {
      expect(offenders).toEqual(["lib/api-fetch.ts"]);
    });

    it("the scan is non-vacuous", () => {
      expect(scannedFiles.length).toBeGreaterThan(50);
    });
  });

  describe("Scenario: the middleware", () => {
    function makeRequest(pathname: string, cookieHeader?: string): NextRequest {
      return new NextRequest(new URL(pathname, "http://localhost:3000"), {
        headers: cookieHeader === undefined ? {} : { cookie: cookieHeader },
      });
    }

    describe("Given a request carrying the session cookie", () => {
      let response: NextResponse;

      beforeAll(() => {
        response = middleware(makeRequest("/dashboard", "genwave-auth=any-value"));
      });

      it("AC5 — still checks cookie presence only (a cookie passes through)", () => {
        expect(response.status).toBe(200);
      });
    });

    describe("Given a request with no session cookie", () => {
      let response: NextResponse;

      beforeAll(() => {
        response = middleware(makeRequest("/dashboard"));
      });

      it("AC5 — still checks cookie presence only (no cookie redirects to /login)", () => {
        expect(new URL(response.headers.get("location") ?? "").pathname).toBe("/login");
      });

      it("the redirect is a 307", () => {
        expect(response.status).toBe(307);
      });
    });
  });

  // ---- sad path ----
  describe("Scenario: a 500 through apiFetch", () => {
    let navigate: jest.MockedFunction<NavigateFn>;
    let rejection: unknown;

    // Given: fetch mocked 500
    beforeEach(async () => {
      global.fetch = jest
        .fn<typeof fetch>()
        .mockResolvedValue(jsonResponse(500)) as unknown as typeof fetch;
      navigate = jest.fn<NavigateFn>();

      rejection = await apiFetch("/api/media/1/vote", { method: "POST", navigate }).then(
        () => undefined,
        (err: unknown) => err
      );
    });

    it("AC6 — rejects with the response", () => {
      expect(rejection).toMatchObject({ status: 500 });
    });

    it("AC6 — does not navigate", () => {
      expect(navigate).not.toHaveBeenCalled();
    });
  });

  describe("Scenario: the session-expired route handler", () => {
    let response: NextResponse;

    beforeEach(() => {
      response = sessionExpiredGet();
    });

    it("GET deletes the cookie", () => {
      expect(response.headers.get("set-cookie")).toBe("genwave-auth=; Path=/; Max-Age=0");
    });

    it("GET redirects with a relative Location (not the container's bind address)", () => {
      expect(response.headers.get("location")).toBe("/login?expired=1");
    });
  });
});
