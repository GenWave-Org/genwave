// STORY-044 — Admin UI: settings server component (node environment)
//
// Runner: Jest (node environment — .ts extension). The server component is called
// directly as an async function; its JSX output is inspected via the same recursive
// tree-walker used in catalog-pages.spec.ts. Fetch is mocked globally to verify
// the correct backend URL is hit.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import type { ReactNode } from "react";

// ---------------------------------------------------------------------------
// Tree walker (mirrors catalog-pages.spec.ts)
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
    if (typeof el.props["href"] === "string") {
      out.push(el.props["href"] as string);
    }
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
// Mock next/headers
// ---------------------------------------------------------------------------

jest.mock("next/headers", () => ({
  cookies: jest.fn().mockResolvedValue({
    toString: () => "session=test-cookie",
  }),
}));

// ---------------------------------------------------------------------------
// Helpers — real wire shapes from GET /api/settings (F2: includes kind + unit)
// ---------------------------------------------------------------------------

interface SettingDto {
  key: string;
  value: string;
  source: "default" | "override";
  applyMode: "live" | "engine-restart";
  kind: "boolean" | "number";
  unit: string;
}

function makeSettingsList(): SettingDto[] {
  return [
    {
      key: "Loudness:TargetLufs",
      value: "-16",
      source: "default",
      applyMode: "live",
      kind: "number",
      unit: "LUFS",
    },
    {
      key: "GW_XFADE_MAX",
      value: "8",
      source: "override",
      applyMode: "engine-restart",
      kind: "number",
      unit: "seconds",
    },
    {
      key: "Station:Cadence:LeadInBeforeEachTrack",
      value: "true",
      source: "default",
      applyMode: "live",
      kind: "boolean",
      unit: "",
    },
  ];
}

function makeFetchMock(
  body: unknown,
  status = 200
): jest.MockedFunction<typeof fetch> {
  const fn = jest
    .fn<typeof fetch>()
    .mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
      headers: new Headers({ "content-type": "application/json" }),
    } as unknown as Response);
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

function capturedUrls(mockFetch: jest.MockedFunction<typeof fetch>): string[] {
  return mockFetch.mock.calls.map(([url]) => String(url));
}

// ---------------------------------------------------------------------------
// Feature: Settings server component
// ---------------------------------------------------------------------------

describe("Feature: Settings server component", () => {
  let originalFetch: typeof fetch;

  beforeEach(() => {
    originalFetch = global.fetch;
    jest.resetModules();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.clearAllMocks();
  });

  describe("Scenario: settings page loads and renders all knobs", () => {
    it("GETs /api/settings", async () => {
      const mockFetch = makeFetchMock(makeSettingsList());

      const { default: SettingsPage } = await import("../app/(authed)/settings/page");
      await SettingsPage({});

      const urls = capturedUrls(mockFetch);
      expect(urls.some((u) => u.includes("/api/settings"))).toBe(true);
    });

    it("has no ad-hoc 'Back to dashboard' link — the shell sidebar owns navigation now (SPEC F28.5)", async () => {
      makeFetchMock(makeSettingsList());

      const { default: SettingsPage } = await import("../app/(authed)/settings/page");
      const node = await SettingsPage({});

      expect(treeContains(node, "Back to dashboard")).toBe(false);
    });

    it("renders an error state when the API returns non-200", async () => {
      makeFetchMock({}, 500);

      const { default: SettingsPage } = await import("../app/(authed)/settings/page");
      const node = await SettingsPage({});

      expect(treeContains(node, "Unable to load settings")).toBe(true);
    });
  });
});
