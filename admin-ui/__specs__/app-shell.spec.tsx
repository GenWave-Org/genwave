// @jest-environment jsdom
// STORY-085 — App shell: sidebar, breadcrumbs, theme switcher (Epic Q / SPEC F28.4–F28.5)
//
// Runner: Jest (jsdom). RTL drives the client shell components (Sidebar,
// Breadcrumbs, ThemeSwitcher's mode control) — mirrors libraries-page.spec.tsx / live-page.spec.tsx
// style. The server-rendered theme-cookie logic (root layout + login page) is
// called directly as an async function and its returned element tree is
// inspected, mirroring the catalog-pages.spec.ts tree-walker house pattern.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";

// ---------------------------------------------------------------------------
// Module mocks. next/jest's SWC transform (unlike babel-jest) does not hoist
// jest.mock() calls above import statements — ES import declarations are
// always evaluated first regardless of source position. So this file never
// statically imports the mocked bindings themselves (that binds too early,
// to the real module); `import type` is erased at compile time and safe,
// and jest.requireMock() below is a plain call that runs in written order,
// after the jest.mock() registrations above it. The shell's client
// components only need usePathname; the sidebar's sign-out form posts to
// the shipped `logout` server action, stubbed so RTL doesn't need a real
// Next.js server-action runtime.
// ---------------------------------------------------------------------------

jest.mock("next/headers", () => ({
  cookies: jest.fn(),
}));

jest.mock("next/navigation", () => ({
  usePathname: jest.fn(),
  redirect: jest.fn(),
}));

jest.mock("@/app/login/actions", () => ({
  logout: jest.fn(),
}));

import { render, screen, fireEvent } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import type { ReactNode } from "react";
import { readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import type { cookies } from "next/headers";
import type { usePathname } from "next/navigation";
import { MODE_COOKIE_NAME } from "../lib/theme";

const ROOT = path.resolve(__dirname, "..");

const mockedCookies = jest.requireMock<{ cookies: typeof cookies }>("next/headers").cookies as jest.MockedFunction<typeof cookies>;
const mockedUsePathname = jest.requireMock<{ usePathname: typeof usePathname }>("next/navigation").usePathname as jest.MockedFunction<typeof usePathname>;

interface FakeCookieStore {
  get: (name: string) => { value: string } | undefined;
}

function mockCookieStore(store: FakeCookieStore): void {
  mockedCookies.mockResolvedValue(store as unknown as Awaited<ReturnType<typeof cookies>>);
}

function noSessionCookieStore(): FakeCookieStore {
  return { get: () => undefined };
}

/** Minimal shape of what jsdom's `matchMedia` fixture returns — only the members the app's
 * theme-mode resolution actually reads (SPEC F102, dark-mode system-preference detection). */
interface MockMediaQueryList {
  matches: boolean;
  media: string;
  addEventListener: jest.Mock;
  removeEventListener: jest.Mock;
  dispatchEvent: jest.Mock;
}

function mockMatchMedia(prefersDark: boolean): void {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    configurable: true,
    value: jest.fn<(query: string) => MockMediaQueryList>().mockImplementation((query) => ({
      matches: prefersDark,
      media: query,
      addEventListener: jest.fn(),
      removeEventListener: jest.fn(),
      dispatchEvent: jest.fn(),
    })),
  });
}

/** Extracts the props bag off a React element returned by an async server component. */
function elementProps(node: ReactNode): Record<string, unknown> {
  const el = node as { props?: Record<string, unknown> };
  if (el === null || typeof el !== "object" || el.props === undefined) {
    throw new Error("expected a React element with props");
  }
  return el.props;
}

/** Collects aria-label values anywhere in a server-component element tree. */
function collectAriaLabels(node: ReactNode, out: string[] = []): string[] {
  if (node === null || node === undefined || typeof node === "boolean") {
    return out;
  }
  if (typeof node === "string" || typeof node === "number") {
    return out;
  }
  if (Array.isArray(node)) {
    for (const child of node) collectAriaLabels(child, out);
    return out;
  }
  const el = node as { props?: Record<string, unknown> };
  if (el && typeof el === "object" && el.props) {
    if (typeof el.props["aria-label"] === "string") {
      out.push(el.props["aria-label"] as string);
    }
    if (el.props["children"] !== undefined) {
      collectAriaLabels(el.props["children"] as ReactNode, out);
    }
  }
  return out;
}

/** Recursively lists files under `dir` with one of `exts`, skipping build/dep dirs. */
function collectFiles(dir: string, exts: string[], out: string[] = []): string[] {
  const SKIP = new Set(["node_modules", ".next"]);
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (SKIP.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      collectFiles(full, exts, out);
    } else if (exts.some((ext) => entry.name.endsWith(ext))) {
      out.push(full);
    }
  }
  return out;
}

beforeEach(() => {
  jest.clearAllMocks();
  document.documentElement.removeAttribute("data-theme");
});

afterEach(() => {
  document.documentElement.removeAttribute("data-theme");
});

// ---------------------------------------------------------------------------
// Feature: App shell
// ---------------------------------------------------------------------------

describe("Feature: App shell", () => {
  describe("Scenario: shell wraps every authed route", () => {
    it("renders the sidebar with Dashboard, Live, Catalog, Station Imaging, Settings (and NOT Libraries)", async () => {
      mockedUsePathname.mockReturnValue("/dashboard");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      render(<Sidebar />);

      for (const label of ["Dashboard", "Live", "Catalog", "Station Imaging", "Settings"]) {
        expect(screen.getByRole("link", { name: label })).toBeInTheDocument();
      }
      // Libraries stays routable but unlisted until Q7.
      expect(screen.queryByRole("link", { name: /libraries/i })).toBeNull();
    });

    it("renders sign-out in the sidebar footer", async () => {
      mockedUsePathname.mockReturnValue("/dashboard");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      render(<Sidebar />);
      const signOut = screen.getByRole("button", { name: /sign out/i });

      expect(signOut.closest("form")).not.toBeNull();
    });

    it("marks the active section with aria-current, including nested routes", async () => {
      mockedUsePathname.mockReturnValue("/catalog/913");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      render(<Sidebar />);

      expect(screen.getByRole("link", { name: "Catalog" })).toHaveAttribute("aria-current", "page");
      expect(screen.getByRole("link", { name: "Dashboard" })).not.toHaveAttribute("aria-current");
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: Persona Catalog nav entry is feature-gated (PLAN T102, SPEC F90.1)", () => {
    it("hides the Persona Catalog link by default (no live signal, fail-closed)", async () => {
      mockedUsePathname.mockReturnValue("/dashboard");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      render(<Sidebar />);

      expect(screen.queryByRole("link", { name: "Community Catalog" })).toBeNull();
    });

    it("lists the Persona Catalog link once the authed layout resolves catalogEnabled=true", async () => {
      mockedUsePathname.mockReturnValue("/dashboard");
      const { Sidebar } = await import("../app/(authed)/_components/Sidebar");

      render(<Sidebar catalogEnabled />);

      expect(screen.getByRole("link", { name: "Community Catalog" })).toHaveAttribute(
        "href",
        "/persona-catalog"
      );
    });

    it("mirrors the same gating in the sub-1024px drawer (MobileNav)", async () => {
      mockedUsePathname.mockReturnValue("/dashboard");
      const { MobileNav } = await import("../app/(authed)/_components/MobileNav");

      render(<MobileNav catalogEnabled />);
      fireEvent.click(screen.getByRole("button", { name: "Open navigation" }));

      await screen.findByRole("dialog", { name: "Navigation" });
      expect(screen.getByRole("link", { name: "Community Catalog" })).toBeInTheDocument();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: breadcrumbs on nested routes only", () => {
    it("shows a Catalog → <mediaId> trail on the track-detail route (Q8 sets the real track title)", async () => {
      mockedUsePathname.mockReturnValue("/catalog/913");
      const { Breadcrumbs } = await import("../app/(authed)/_components/Breadcrumbs");

      render(<Breadcrumbs />);

      const trail = screen.getByRole("navigation", { name: /breadcrumb/i });
      expect(trail).toHaveTextContent("Catalog");
      expect(trail).toHaveTextContent("913");
      expect(screen.getByRole("link", { name: "Catalog" })).toHaveAttribute("href", "/catalog");
    });

    it("shows no breadcrumb on top-level pages", async () => {
      mockedUsePathname.mockReturnValue("/dashboard");
      const { Breadcrumbs } = await import("../app/(authed)/_components/Breadcrumbs");

      const { container } = render(<Breadcrumbs />);

      expect(container).toBeEmptyDOMElement();
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: mode defaults to system and the toggle persists", () => {
    it("follows prefers-color-scheme when no genwave-mode cookie exists (no data-theme attribute rendered)", async () => {
      mockCookieStore(noSessionCookieStore());
      const { default: RootLayout } = await import("../app/layout");

      const html = await RootLayout({ children: "content" });

      expect(elementProps(html)["data-theme"]).toBeUndefined();
    });

    it("stores the toggled choice in the genwave-mode cookie and flips <html> immediately", async () => {
      mockMatchMedia(false); // system prefers light
      const { ThemeSwitcher } = await import("../app/(authed)/_components/ThemeSwitcher");

      render(<ThemeSwitcher choices={[]} stationThemeSlug="" />);
      const button = await screen.findByRole("button", { name: /switch to dark theme/i });

      fireEvent.click(button);

      expect(document.cookie).toContain(`${MODE_COOKIE_NAME}=dark`);
      expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    });

    it("stamps data-theme on <html> during the server render (no wrong-mode flash)", async () => {
      mockCookieStore({ get: (name) => (name === MODE_COOKIE_NAME ? { value: "dark" } : undefined) });
      const { default: RootLayout } = await import("../app/layout");

      const html = await RootLayout({ children: "content" });

      expect(elementProps(html)["data-theme"]).toBe("dark");
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: ad-hoc navigation is gone", () => {
    it("has zero 'Back to ...' cross-nav links under app/", () => {
      const files = collectFiles(path.join(ROOT, "app"), [".tsx"]);
      const offenders = files
        .filter((f) => readFileSync(f, "utf-8").includes("Back to"))
        .map((f) => path.relative(ROOT, f));

      expect(offenders).toEqual([]);
    });
  });

  // ---------------------------------------------------------------------------
  describe("Scenario: login is shell-less but on-identity", () => {
    it("renders login without the sidebar", async () => {
      mockCookieStore(noSessionCookieStore());
      const { default: LoginPage } = await import("../app/login/page");

      const node = await LoginPage({ searchParams: Promise.resolve({}) });

      // The Sidebar's nav landmark is uniquely labelled "Sections" — its absence
      // proves the login page never mounts the shell.
      expect(collectAriaLabels(node)).not.toContain("Sections");
    });

    it("applies Wireless tokens, the tokenized Button and the Fraunces wordmark on the login page", () => {
      const pageSrc = readFileSync(path.join(ROOT, "app", "login", "page.tsx"), "utf-8");
      const formSrc = readFileSync(path.join(ROOT, "app", "login", "LoginForm.tsx"), "utf-8");

      expect(pageSrc).toMatch(/bg-bg/);
      expect(pageSrc).toMatch(/font-display/);
      expect(pageSrc).toMatch(/italic/);
      expect(formSrc).toMatch(/from ["']@\/components\/ui\/button["']/);

      // Tokens only — no raw hex or Tailwind stock palette classes.
      const STOCK_PALETTE_PATTERN = /\b(?:bg-orange-|text-gray-|bg-gray-)\S*/;
      expect(pageSrc).not.toMatch(/#[0-9a-fA-F]{3,6}/);
      expect(pageSrc).not.toMatch(STOCK_PALETTE_PATTERN);
    });
  });

  // ---------------------------------------------------------------------------
  // SAD PATH
  // ---------------------------------------------------------------------------

  describe("Scenario: rejecting a garbage mode cookie (sad path)", () => {
    it("falls back to prefers-color-scheme when genwave-mode holds an invalid value", async () => {
      mockCookieStore({ get: (name) => (name === MODE_COOKIE_NAME ? { value: "banana" } : undefined) });
      const { default: RootLayout } = await import("../app/layout");

      const html = await RootLayout({ children: "content" });

      expect(elementProps(html)["data-theme"]).toBeUndefined();
    });
  });
});
