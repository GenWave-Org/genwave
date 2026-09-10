/**
 * A small `global.fetch` route table — method + pathname predicate → response — shared by
 * `ads-page.spec.tsx` and `spot-wizard.spec.tsx` (PLAN T448).
 *
 * Born in `ads-page.spec.tsx` (STORY-392, generalizing `gardener-tabs.spec.tsx`'s own inline
 * if-chain once the Ads page's surface grew past a handful of routes — create/edit/approve/
 * retry/retire, voices, two briefs endpoints), then hoisted here so `spot-wizard.spec.tsx` could
 * import it for the SpotWizard's own smaller route surface instead of copying it byte-for-byte.
 * It is not the codebase's one authored route-table implementation: other specs (among them
 * `ads-sponsor-rail.spec.tsx`, a 1-arg `respond` variant) still declare their own fetch mocks
 * of differing shapes — this module is only what these two files share.
 *
 * `apiGet` (a page's own server-side reads) always hands an absolute `BACKEND_URL`-prefixed
 * request; every browser-side `*-api.ts` fetcher hands a bare relative path instead — a base
 * origin lets `URL()` parse both the same way real `fetch()` resolution would (the
 * `gardener-tabs.spec.tsx` precedent).
 */

import { jest } from "@jest/globals";

export interface RouteResponseSpec {
  status: number;
  body?: unknown;
}

export interface RouteHandler {
  method: string;
  match: (url: URL) => boolean;
  respond: (url: URL, init: RequestInit | undefined) => RouteResponseSpec;
}

function toResponse(spec: RouteResponseSpec): Response {
  return {
    ok: spec.status >= 200 && spec.status < 300,
    status: spec.status,
    json: jest.fn<() => Promise<unknown>>().mockResolvedValue(spec.body ?? {}),
    headers: new Headers({ "content-type": "application/json" }),
  } as unknown as Response;
}

/** Installs `global.fetch` as a route table, throwing on any call that matches no handler — a
 * spec's own list of expected requests doubles as an assertion that nothing else was called. */
export function installFetchMock(handlers: RouteHandler[]): jest.MockedFunction<typeof fetch> {
  const fn = jest.fn<typeof fetch>().mockImplementation(async (input, init) => {
    const method = init?.method ?? "GET";
    const url = new URL(String(input), "http://localhost");
    const handler = handlers.find((h) => h.method === method && h.match(url));
    if (handler === undefined) {
      throw new Error(`unexpected fetch call: ${method} ${url.pathname}${url.search}`);
    }
    return toResponse(handler.respond(url, init));
  });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}
