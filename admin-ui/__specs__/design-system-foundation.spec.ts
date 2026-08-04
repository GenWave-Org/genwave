// STORY-083 — Design-system foundation: tokens, fonts, component library (Epic Q / SPEC F28.1–F28.3)
//
// Runner: Jest (node environment — file-content assertions over the token layer,
// vendored fonts, and component sources). Landed via Q1 (/build-loop).

import { describe, it, expect } from "@jest/globals";
import { existsSync, readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { AA_NORMAL_TEXT_MIN_CONTRAST, contrastRatio } from "./contrast-ratio";

const ROOT = path.resolve(__dirname, "..");

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Recursively collects file paths under `dir` with one of `exts`, skipping build/dep dirs. */
function collectFiles(dir: string, exts: string[], out: string[] = []): string[] {
  const SKIP = new Set(["node_modules", ".next", "__specs__", ".git"]);
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

/** Extracts the declaration block body for a top-level CSS selector, e.g. `:root {`. */
function extractBlock(css: string, selector: string): string {
  const start = css.indexOf(selector);
  if (start === -1) {
    throw new Error(`selector not found: ${selector}`);
  }
  const braceStart = css.indexOf("{", start);
  let depth = 0;
  let i = braceStart;
  for (; i < css.length; i++) {
    if (css[i] === "{") depth++;
    if (css[i] === "}") {
      depth--;
      if (depth === 0) break;
    }
  }
  return css.slice(braceStart + 1, i);
}

const WIRELESS_TOKEN_NAMES = [
  "--bg",
  "--surface",
  "--surface-2",
  "--line",
  "--ink",
  "--mute",
  "--accent",
  "--accent-ink",
  "--accent-2",
  "--danger",
  "--danger-ink",
  "--success",
] as const;

const globalsCssPath = path.join(ROOT, "app", "globals.css");
const globalsCss = readFileSync(globalsCssPath, "utf-8");

const STOCK_PALETTE_PATTERN = /\b(?:bg-orange-|text-gray-|bg-gray-)\S*/g;

/** Reads a `--token: #rrggbb` value out of an already-extracted CSS block. */
function tokenValue(block: string, token: string): string {
  const value = block.match(new RegExp(`${token}\\s*:\\s*(#[0-9a-fA-F]{6})`))?.[1];
  if (!value) {
    throw new Error(`token not found: ${token}`);
  }
  return value;
}

describe("Feature: Design-system foundation", () => {
  describe("Scenario: token sets exist for both themes", () => {
    it('defines the Wireless light palette as semantic custom properties under :root', () => {
      const rootBlock = extractBlock(globalsCss, ":root {");
      for (const token of WIRELESS_TOKEN_NAMES) {
        expect(rootBlock).toMatch(new RegExp(`${token}\\s*:\\s*#[0-9a-fA-F]{6}`));
      }
    });

    it('defines the walnut-and-brass dark palette under :root[data-theme="dark"]', () => {
      const darkBlock = extractBlock(globalsCss, ':root[data-theme="dark"] {');
      for (const token of WIRELESS_TOKEN_NAMES) {
        expect(darkBlock).toMatch(new RegExp(`${token}\\s*:\\s*#[0-9a-fA-F]{6}`));
      }
    });

    it("dark palette values differ from the light palette (a tuned set, not a shared default)", () => {
      const rootBlock = extractBlock(globalsCss, ":root {");
      const darkBlock = extractBlock(globalsCss, ':root[data-theme="dark"] {');
      for (const token of WIRELESS_TOKEN_NAMES) {
        const lightValue = rootBlock.match(new RegExp(`${token}\\s*:\\s*(#[0-9a-fA-F]{6})`))?.[1];
        const darkValue = darkBlock.match(new RegExp(`${token}\\s*:\\s*(#[0-9a-fA-F]{6})`))?.[1];
        expect(darkValue).toBeDefined();
        expect(darkValue).not.toBe(lightValue);
      }
    });

    it("maps the semantic tokens into Tailwind via @theme", () => {
      const themeBlock = extractBlock(globalsCss, "@theme inline {");
      for (const token of WIRELESS_TOKEN_NAMES) {
        const colorKey = `--color-${token.slice(2)}`;
        expect(themeBlock).toMatch(new RegExp(`${colorKey}\\s*:\\s*var\\(${token}\\)`));
      }
    });
  });

  // Q3 review advisory, folded into Q11 (STORY-093): the system-dark fallback
  // (nobody has an explicit genwave-mode cookie, OS prefers dark) duplicates
  // :root[data-theme="dark"]'s values verbatim, since CSS custom properties
  // have no block-reuse mechanism (SPEC F28.4, see globals.css's own comment
  // there). A future dark-theme token tweak that only touches one of the two
  // blocks would silently desync system-default dark from explicit dark —
  // this guard fails the moment that happens.
  describe("Scenario: the prefers-color-scheme dark fallback stays in sync with the explicit dark theme", () => {
    it("has an identical token set to :root[data-theme=\"dark\"] for every Wireless token", () => {
      const darkBlock = extractBlock(globalsCss, ':root[data-theme="dark"] {');
      const systemDarkBlock = extractBlock(globalsCss, ":root:not([data-theme]) {");

      for (const token of WIRELESS_TOKEN_NAMES) {
        const darkValue = tokenValue(darkBlock, token);
        const systemDarkValue = tokenValue(systemDarkBlock, token);
        expect(systemDarkValue).toBe(darkValue);
      }
    });
  });

  describe("Scenario: on-accent and on-danger ink meet AA contrast in both themes", () => {
    it("accent/accent-ink pass 4.5:1 in the light theme", () => {
      const rootBlock = extractBlock(globalsCss, ":root {");
      const ratio = contrastRatio(tokenValue(rootBlock, "--accent"), tokenValue(rootBlock, "--accent-ink"));
      expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
    });

    it("accent/accent-ink pass 4.5:1 in the dark theme", () => {
      const darkBlock = extractBlock(globalsCss, ':root[data-theme="dark"] {');
      const ratio = contrastRatio(tokenValue(darkBlock, "--accent"), tokenValue(darkBlock, "--accent-ink"));
      expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
    });

    it("danger/danger-ink pass 4.5:1 in the light theme", () => {
      const rootBlock = extractBlock(globalsCss, ":root {");
      const ratio = contrastRatio(tokenValue(rootBlock, "--danger"), tokenValue(rootBlock, "--danger-ink"));
      expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
    });

    it("danger/danger-ink pass 4.5:1 in the dark theme", () => {
      const darkBlock = extractBlock(globalsCss, ':root[data-theme="dark"] {');
      const ratio = contrastRatio(tokenValue(darkBlock, "--danger"), tokenValue(darkBlock, "--danger-ink"));
      expect(ratio).toBeGreaterThanOrEqual(AA_NORMAL_TEXT_MIN_CONTRAST);
    });
  });

  // PLAN T173 (SPEC F102, ARCHITECTURE.md "Theme system" decision table): the three .woff2
  // binaries are no longer duplicated into admin-ui at all — GenWave.Host/wwwroot/fonts is their
  // one home, served at the canonical GET /fonts/{file} both surfaces read from (admin via
  // next.config.ts's rewrite). next/font/local is gone: a content-hashed .next/static/media/* URL
  // can never be named in a theme manifest (T156's FontSrcPattern), so --font-display/--font-sans
  // are now plain @font-face-backed custom properties in globals.css, matching spectator's own
  // styles.css exactly.
  describe("Scenario: fonts are vendored on the backend and served through one canonical route", () => {
    const fontsDir = path.resolve(ROOT, "..", "src", "GenWave.Host", "wwwroot", "fonts");
    const fontFiles = existsSync(fontsDir) ? readdirSync(fontsDir) : [];

    it("ships the Fraunces normal + italic woff2 faces on the backend, lowercase-hyphenated", () => {
      expect(fontFiles).toContain("fraunces-variable-latin.woff2");
      expect(fontFiles).toContain("fraunces-italic-variable-latin.woff2");
      for (const f of ["fraunces-variable-latin.woff2", "fraunces-italic-variable-latin.woff2"]) {
        expect(statSync(path.join(fontsDir, f)).size).toBeGreaterThan(0);
      }
    });

    it("ships the Source Sans 3 woff2 face on the backend, lowercase-hyphenated", () => {
      expect(fontFiles).toContain("source-sans-3-variable-latin.woff2");
      expect(statSync(path.join(fontsDir, "source-sans-3-variable-latin.woff2")).size).toBeGreaterThan(0);
    });

    it("no .woff2 binary is duplicated into admin-ui itself", () => {
      const adminFontsDir = path.join(ROOT, "app", "fonts");
      expect(existsSync(adminFontsDir)).toBe(false);
    });

    it("declares @font-face rules in globals.css pointing at the canonical /fonts route, not next/font/local", () => {
      const layoutSrc = readFileSync(path.join(ROOT, "app", "layout.tsx"), "utf-8");
      expect(layoutSrc).not.toMatch(/from\s+["']next\/font/);

      expect(globalsCss).toMatch(/@font-face/);
      expect(globalsCss).toMatch(/"Fraunces"/);
      expect(globalsCss).toMatch(/"Source Sans 3"/);
      expect(globalsCss).toMatch(/url\(["']\/fonts\/fraunces-variable-latin\.woff2["']\)/);
      expect(globalsCss).toMatch(/url\(["']\/fonts\/fraunces-italic-variable-latin\.woff2["']\)/);
      expect(globalsCss).toMatch(/url\(["']\/fonts\/source-sans-3-variable-latin\.woff2["']\)/);
      expect(globalsCss).toMatch(/--font-display:\s*"Fraunces"/);
      expect(globalsCss).toMatch(/--font-sans:\s*"Source Sans 3"/);
    });

    it("rewrites /fonts/:path* to the backend, alongside the existing /api/:path* rewrite", () => {
      const nextConfigSrc = readFileSync(path.join(ROOT, "next.config.ts"), "utf-8");
      expect(nextConfigSrc).toMatch(/source:\s*["']\/fonts\/:path\*["']/);
    });

    it("emits no external font URL in the build output or source", () => {
      const EXTERNAL_FONT_PATTERN = /fonts\.googleapis\.com|fonts\.gstatic\.com/;

      // Source: app/, components/, lib/ — the code that could ever emit such a request.
      const sourceFiles = collectFiles(ROOT, [".ts", ".tsx", ".css"]).filter(
        (f) => !f.startsWith(path.join(ROOT, "node_modules"))
      );
      for (const file of sourceFiles) {
        expect(readFileSync(file, "utf-8")).not.toMatch(EXTERNAL_FONT_PATTERN);
      }

      // Build output, if present: only our own app/static output, not Next's
      // vendored framework internals (next/font/google's own unreachable module).
      const nextStatic = path.join(ROOT, ".next", "static");
      const nextServerApp = path.join(ROOT, ".next", "server", "app");
      for (const dir of [nextStatic, nextServerApp]) {
        if (!existsSync(dir)) continue;
        for (const file of collectFiles(dir, [".js", ".css", ".html"])) {
          expect(readFileSync(file, "utf-8")).not.toMatch(EXTERNAL_FONT_PATTERN);
        }
      }
    });
  });

  describe("Scenario: shadcn/ui is scaffolded on the tokens", () => {
    it("renders a primitive whose colors resolve from semantic tokens", async () => {
      const { buttonVariants } = await import("../components/ui/button");
      const primaryClasses = buttonVariants({ variant: "primary" });
      const secondaryClasses = buttonVariants({ variant: "secondary" });
      const destructiveClasses = buttonVariants({ variant: "destructive" });

      // Token-driven utility classes, not raw colors.
      expect(primaryClasses).toMatch(/\bbg-accent\b/);
      expect(secondaryClasses).toMatch(/\bborder-line\b/);
      expect(secondaryClasses).toMatch(/\bbg-surface\b/);
      expect(destructiveClasses).toMatch(/\bbg-danger\b/);

      // Never a raw hex value or stock palette class.
      for (const classes of [primaryClasses, secondaryClasses, destructiveClasses]) {
        expect(classes).not.toMatch(/#[0-9a-fA-F]{3,6}/);
        expect(classes).not.toMatch(STOCK_PALETTE_PATTERN);
      }
    });

    it("uses the dedicated on-accent/on-danger ink tokens, not --surface/--ink", async () => {
      const { buttonVariants } = await import("../components/ui/button");
      const primaryClasses = buttonVariants({ variant: "primary" });
      const destructiveClasses = buttonVariants({ variant: "destructive" });

      expect(primaryClasses).toMatch(/\btext-accent-ink\b/);
      expect(destructiveClasses).toMatch(/\btext-danger-ink\b/);

      // --surface/--ink flip meaning per theme; they must never carry on-accent/on-danger text.
      expect(primaryClasses).not.toMatch(/\btext-surface\b/);
      expect(destructiveClasses).not.toMatch(/\btext-surface\b/);
    });
  });

  describe("Scenario: rejecting raw palette classes (sad path)", () => {
    it("has zero Tailwind stock-palette classes (bg-orange-/text-gray-/bg-gray-) in app/ and components/", () => {
      const files = [
        ...collectFiles(path.join(ROOT, "app"), [".ts", ".tsx"]),
        ...collectFiles(path.join(ROOT, "components"), [".ts", ".tsx"]),
      ];

      const offenders: string[] = [];
      for (const file of files) {
        const content = readFileSync(file, "utf-8");
        const matches = content.match(STOCK_PALETTE_PATTERN);
        if (matches) {
          offenders.push(`${path.relative(ROOT, file)}: ${matches.join(", ")}`);
        }
      }

      expect(offenders).toEqual([]);
    });
  });
});
