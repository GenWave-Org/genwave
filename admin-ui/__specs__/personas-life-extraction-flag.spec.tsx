// @jest-environment jsdom
// T720 — the Personas page derives `lifeExtractionOff` from the settings it fetches server-side.

jest.mock("next/headers", () => ({
  cookies: jest.fn<() => Promise<{ toString: () => string }>>().mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

import { describe, it, expect, beforeEach, afterEach, jest } from "@jest/globals";
import type { ReactNode } from "react";
import { lifeExtractionOffFrom } from "../app/(authed)/personas/life-extraction-setting";

const KEY = "Memory:LifeExtraction:Enabled";

function jsonResponse(body: unknown, ok = true): Response {
  return {
    ok,
    status: ok ? 200 : 500,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(body),
    headers: new Headers(),
  } as unknown as Response;
}

function findProps(node: ReactNode, key: string): Record<string, unknown> | null {
  if (node === null || node === undefined || typeof node !== "object") return null;
  if (Array.isArray(node)) {
    for (const child of node) {
      const found = findProps(child, key);
      if (found !== null) return found;
    }
    return null;
  }
  const el = node as { props?: Record<string, unknown> };
  if (el.props && key in el.props) return el.props;
  return el.props?.["children"] !== undefined ? findProps(el.props["children"] as ReactNode, key) : null;
}

describe("lifeExtractionOffFrom", () => {
  it.each([
    ["false", true],
    [" False ", true],
    ["true", false],
    ["", false],
  ])("row value %p -> %p", (value, expected) => {
    expect(lifeExtractionOffFrom([{ key: KEY, value }])).toBe(expected);
  });

  it("is false for a missing row or a malformed body", () => {
    expect(lifeExtractionOffFrom([{ key: "Other", value: "false" }])).toBe(false);
    expect(lifeExtractionOffFrom({ nope: true })).toBe(false);
    expect(lifeExtractionOffFrom(null)).toBe(false);
  });
});

describe("PersonasPage lifeExtractionOff prop", () => {
  const originalFetch = global.fetch;
  beforeEach(() => {
    jest.resetModules();
  });
  afterEach(() => {
    global.fetch = originalFetch;
  });

  async function pagePropWith(settings: () => Promise<Response>): Promise<unknown> {
    global.fetch = jest.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes("/api/settings")) return settings();
      if (url.includes("/api/personas")) return jsonResponse([]);
      if (url.includes("/api/schedule")) return jsonResponse({ segments: [] });
      return jsonResponse({ llm: { activePersona: null } });
    }) as unknown as typeof fetch;
    const { default: PersonasPage } = await import("../app/(authed)/personas/page");
    return findProps(await PersonasPage(), "initialPersonas")?.["lifeExtractionOff"];
  }

  it("is true when the setting row is false", async () => {
    expect(await pagePropWith(async () => jsonResponse([{ key: KEY, value: "false" }]))).toBe(true);
  });

  it("is false when the setting row is true", async () => {
    expect(await pagePropWith(async () => jsonResponse([{ key: KEY, value: "true" }]))).toBe(false);
  });

  it("is false when the settings fetch rejects", async () => {
    expect(
      await pagePropWith(async () => {
        throw new Error("network down");
      })
    ).toBe(false);
  });

  it("is false when the settings read is a non-2xx", async () => {
    expect(await pagePropWith(async () => jsonResponse([{ key: KEY, value: "false" }], false))).toBe(false);
  });
});
