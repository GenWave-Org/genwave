// STORY-264 — 🔌 wire: composed CSS linked from the admin root layout (SPEC F102.3, PLAN T162)
//
// Runner: Jest (node environment — .ts extension). RootLayout is an async server component
// (uses next/headers) — called directly as a plain function and its returned React element
// tree inspected, mirroring app-shell.spec.tsx / settings-server.spec.ts's own house pattern
// (Server Components can't run under RTL).
//
// PLAN T162's hard precondition: the composed sheet's flat `:root` TIES globals.css's own
// `:root` on CSS specificity — (0,1,0) each — so "composed wins" holds only because the
// composed sheet loads AFTER globals.css. Unlike wwwroot/spectator/index.html's two literal
// <link> tags (order = document order, asserted directly in Story264_ComposedStylesheet.cs),
// globals.css here is a Next.js-managed CSS import, not a JSX node this file's tree-walker can
// see — so there is no "second <link>" to compare position against under Jest. What actually
// keeps order correct in the real browser is React 19's `<link precedence>` Resource API:
// Next.js registers its own bundled stylesheet as a precedence-managed resource before this
// component's JSX is ever walked, and React appends any NEWLY-seen precedence value as a LATER
// group in <head> — so declaring our OWN precedence value is what places this stylesheet after
// globals.css, regardless of where in the tree the <link> is rendered. That is the load-bearing
// fact this suite pins: drop the `precedence` prop (the actual T162 "reversal") and this suite
// goes red, because the ordering guarantee it asserts no longer holds. Real document order is
// verified against a live BUILD=1 stack (PLAN T162 definition of done) — Jest cannot reproduce
// Next's build-time CSS injection.

import { describe, it, expect, jest, beforeEach } from "@jest/globals";
import type { ReactNode } from "react";

// ---------------------------------------------------------------------------
// Tree walker: collect every <link> element's props from a React element tree
// returned by an async server component (mirrors catalog-pages.spec.ts).
// ---------------------------------------------------------------------------

interface LinkProps {
  href?: unknown;
  rel?: unknown;
  precedence?: unknown;
}

function collectLinkProps(node: ReactNode, out: LinkProps[] = []): LinkProps[] {
  if (node === null || node === undefined || typeof node === "boolean") {
    return out;
  }
  if (typeof node === "string" || typeof node === "number") {
    return out;
  }
  if (Array.isArray(node)) {
    for (const child of node) collectLinkProps(child, out);
    return out;
  }
  const el = node as { type?: unknown; props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props) {
    if (el.type === "link") {
      out.push({ href: el.props["href"], rel: el.props["rel"], precedence: el.props["precedence"] });
    }
    if (el.props["children"] !== undefined) {
      collectLinkProps(el.props["children"] as ReactNode, out);
    }
  }
  return out;
}

// ---------------------------------------------------------------------------
// Mock next/headers
// ---------------------------------------------------------------------------

jest.mock("next/headers", () => ({
  cookies: jest.fn(),
}));

import type { cookies } from "next/headers";

const mockedCookies = jest
  .requireMock<{ cookies: typeof cookies }>("next/headers")
  .cookies as jest.MockedFunction<typeof cookies>;

interface FakeCookieStore {
  get: (name: string) => { value: string } | undefined;
}

function mockCookieStore(store: FakeCookieStore): void {
  mockedCookies.mockResolvedValue(store as unknown as Awaited<ReturnType<typeof cookies>>);
}

function noSessionCookieStore(): FakeCookieStore {
  return { get: () => undefined };
}

beforeEach(() => {
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Feature: composed stylesheet wired into the admin root layout
// ---------------------------------------------------------------------------

describe("Feature: admin root layout links the composed theme stylesheet", () => {
  describe("Scenario: every page reaches the composed sheet through the admin route", () => {
    it("renders a stylesheet link to /api/theme.css", async () => {
      mockCookieStore(noSessionCookieStore());
      const { default: RootLayout } = await import("../app/layout");

      const html = await RootLayout({ children: "content" });

      const links = collectLinkProps(html);
      const themeLink = links.find((l) => l.href === "/api/theme.css");
      expect(themeLink).toBeDefined();
      expect(themeLink?.rel).toBe("stylesheet");
    });

    it("still renders the link when no session cookie exists (root layout wraps /login too)", async () => {
      mockCookieStore(noSessionCookieStore());
      const { default: RootLayout } = await import("../app/layout");

      const html = await RootLayout({ children: "content" });

      const links = collectLinkProps(html);
      expect(links.some((l) => l.href === "/api/theme.css")).toBe(true);
    });
  });

  describe("Scenario: the link carries the ordering guarantee PLAN T162's hard precondition requires", () => {
    it("declares a stylesheet precedence, the mechanism React uses to place it after globals.css", async () => {
      mockCookieStore(noSessionCookieStore());
      const { default: RootLayout } = await import("../app/layout");

      const html = await RootLayout({ children: "content" });

      const links = collectLinkProps(html);
      const themeLink = links.find((l) => l.href === "/api/theme.css");
      // A missing/empty precedence turns this into a plain, unordered <link> — React no
      // longer guarantees it loads after globals.css's own precedence-managed stylesheet.
      // This is the literal "reversed link order" PLAN T162 asks for a spec to catch.
      expect(typeof themeLink?.precedence).toBe("string");
      expect(themeLink?.precedence).not.toBe("");
    });
  });
});
