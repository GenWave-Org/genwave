// STORY-268 — The shelf and its AA gate (SPEC F102.1, F102.8)
//
// Runner: Jest (node environment — data assertions over the theme manifests).
//
// ⚠️ REUSE, DO NOT REIMPLEMENT: `contrast-ratio.ts` owns the ONE `contrastRatio`
// implementation (it is what proved --accent-2 was below AA against all three light
// grounds, and why dark deliberately inverts --accent-ink). This file imports it rather
// than growing a second, subtly different contrast implementation.
//
// Why data-driven: 6+ themes × 2 modes × the asserted token pairs is not hand-checkable,
// and the failure mode is a 3.9:1 pair shipping unnoticed in theme #5. Iterating the
// SHIPPED MANIFESTS (JSON, not parsed CSS) means adding a seventh theme cannot skip the
// gate with no change to this file.
//
// ⚠️ MEMBERSHIP, NOT KEY-ITERATION (PLAN T158 hard precondition, ThemeManifestParser.
// ParseModes review finding): every lookup below asks a mode object for a NAMED token out
// of the fixed 18-name vocabulary (12 semantic colours + 6 --sched-*) — it never iterates
// `Object.keys(mode)`. T156 already guards cross-mode PARITY (neither mode may define a
// token the other lacks), but a manifest omitting e.g. `accent-ink` from BOTH modes passes
// parity clean. That absence then hits the one decision that hides it: the shipped
// default's tokens stay in the static stylesheets with `theme.css` layered on top, so the
// missing token silently resolves to cream-enamel's `accent-ink` painted onto the new
// theme's `accent`. It renders fine, so nobody sees it — a gate that iterated present keys
// would SKIP that pair rather than fail it. Naming the token forces a lookup that throws
// the moment it is absent, in either mode, regardless of what other keys the manifest
// happens to carry.
//
// ⚠️ AC1 IS KNOWN-RED BY RULING THROUGH SHIP 1, NOT A REGRESSION. Ship 1 (T156–T170)
// delivers the mechanism carrying ONE theme — today's palette as its light+dark modes.
// F102.1's "at least six" goes green at T171 (Ship 2). A reader seeing this fail during
// Ship 1 is seeing the recorded plan, not a defect.

import { describe, it, expect } from "@jest/globals";
import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { AA_NORMAL_TEXT_MIN_CONTRAST, contrastRatio } from "./contrast-ratio";

// ---------------------------------------------------------------------------
// The 18-name token vocabulary (PLAN T158 hard precondition; count corrected
// 2026-08-03 — 12 semantic colours, not 13; --font-display/--font-sans are font-family
// lists living in the manifest's `fonts` object, not colour tokens).
// ---------------------------------------------------------------------------

const SEMANTIC_TOKEN_NAMES = [
  "bg",
  "surface",
  "surface-2",
  "line",
  "ink",
  "mute",
  "accent",
  "accent-ink",
  "accent-2",
  "danger",
  "danger-ink",
  "success",
] as const;

const SCHEDULE_TOKEN_NAMES = [
  "sched-1",
  "sched-2",
  "sched-3",
  "sched-4",
  "sched-5",
  "sched-6",
] as const;

const TOKEN_VOCABULARY = [...SEMANTIC_TOKEN_NAMES, ...SCHEDULE_TOKEN_NAMES] as const;

type TokenName = (typeof TOKEN_VOCABULARY)[number];

/** The three surfaces body ink (and the secondary tokens) render text against. */
const GROUNDS: readonly TokenName[] = ["bg", "surface", "surface-2"];

type Mode = "light" | "dark";
const MODES: readonly Mode[] = ["light", "dark"];

// ---------------------------------------------------------------------------
// Loading the shipped manifests — JSON data, never a parsed CSS declaration block.
// ---------------------------------------------------------------------------

const THEMES_DIR = path.resolve(
  __dirname,
  "..",
  "..",
  "src",
  "GenWave.Host",
  "Theming",
  "themes"
);

interface ThemeManifestFixture {
  readonly slug: string;
  readonly modes: {
    readonly light: Readonly<Record<string, string>>;
    readonly dark: Readonly<Record<string, string>>;
  };
}

function isTokenMap(v: unknown): v is Readonly<Record<string, string>> {
  return (
    typeof v === "object" &&
    v !== null &&
    !Array.isArray(v) &&
    Object.values(v).every((value) => typeof value === "string")
  );
}

/** Validates the untrusted parsed JSON has exactly the shape this gate needs — a slug and
 * both modes as string-keyed token maps. Full manifest validation (fonts, name, author, CSS-
 * safe shapes) is `ThemeManifestParser`'s job on the C# side; this is deliberately narrower. */
function assertIsThemeManifestFixture(
  v: unknown,
  sourceFile: string
): asserts v is ThemeManifestFixture {
  if (typeof v !== "object" || v === null) {
    throw new Error(`theme manifest '${sourceFile}' did not parse to an object`);
  }
  const record = v as Record<string, unknown>;

  if (typeof record.slug !== "string" || record.slug.length === 0) {
    throw new Error(`theme manifest '${sourceFile}' is missing a slug`);
  }

  const modes = record.modes;
  if (typeof modes !== "object" || modes === null) {
    throw new Error(`theme '${record.slug}' is missing its 'modes' object`);
  }
  const modesRecord = modes as Record<string, unknown>;

  if (!isTokenMap(modesRecord.light)) {
    throw new Error(`theme '${record.slug}' is missing a 'light' token map`);
  }
  if (!isTokenMap(modesRecord.dark)) {
    throw new Error(`theme '${record.slug}' is missing a 'dark' token map`);
  }
}

/** Reads every `*.json` manifest in the shipped themes directory — this is the entire
 * "shelf" the gate measures. A file added here needs no change to this spec (AC9). */
function loadThemeManifests(): ThemeManifestFixture[] {
  const files = readdirSync(THEMES_DIR).filter((f) => f.endsWith(".json"));
  return files.map((file) => {
    const raw: unknown = JSON.parse(readFileSync(path.join(THEMES_DIR, file), "utf-8"));
    assertIsThemeManifestFixture(raw, file);
    return raw;
  });
}

// ---------------------------------------------------------------------------
// The gate itself — a named-lookup, never a keys-iteration (the hard precondition).
// ---------------------------------------------------------------------------

/** Looks up ONE named token out of the fixed vocabulary. Throws — naming the theme, the
 * mode and the token — the moment it is absent, rather than silently skipping it the way
 * iterating `Object.keys(mode)` would. This is the precondition's whole mechanism. */
function requireToken(manifest: ThemeManifestFixture, mode: Mode, token: TokenName): string {
  const value = manifest.modes[mode][token];
  if (value === undefined) {
    throw new Error(
      `theme '${manifest.slug}' mode '${mode}' is missing required token '${token}' out of the ` +
        `18-name vocabulary — a token absent from BOTH modes must fail this gate, not fall through ` +
        `to the static stylesheet's cream-enamel default (SPEC F102.8 precondition)`
    );
  }
  return value;
}

/** Asserts every one of the 18 vocabulary tokens is present in one theme's one mode —
 * membership against the fixed vocabulary, not "whatever keys this manifest happens to
 * carry" (AC2, the hard precondition). */
function assertModeHasFullVocabulary(manifest: ThemeManifestFixture, mode: Mode): void {
  for (const token of TOKEN_VOCABULARY) {
    requireToken(manifest, mode, token);
  }
}

/** Measures one foreground/background token pair against AA (4.5:1). Throws — naming the
 * theme, the mode, the pair and the measured ratio — the moment it fails (STORY-268 AC8). */
function assertPairMeetsAA(
  manifest: ThemeManifestFixture,
  mode: Mode,
  foreground: TokenName,
  background: TokenName
): void {
  const fg = requireToken(manifest, mode, foreground);
  const bg = requireToken(manifest, mode, background);
  const ratio = contrastRatio(fg, bg);
  if (ratio < AA_NORMAL_TEXT_MIN_CONTRAST) {
    throw new Error(
      `AA contrast gate failed — theme '${manifest.slug}', mode '${mode}', pair ` +
        `'${foreground}' on '${background}': measured ${ratio.toFixed(2)}:1, need at least ` +
        `${AA_NORMAL_TEXT_MIN_CONTRAST}:1`
    );
  }
}

// ---------------------------------------------------------------------------
// Synthetic fixtures for the gate's OWN sad-path/generality specs (AC8, AC9) — deliberately
// not the real shipped manifests, so these prove the gate's mechanism rather than restate
// the happy-path checks against cream-enamel.
// ---------------------------------------------------------------------------

/** A compile-time-complete token map — assigning an object literal to `Record<TokenName,
 * string>` forces every one of the 18 vocabulary names to be present, so a typo or an
 * omission here is a `tsc` error, not a runtime surprise in AC8/AC9 below. */
type CompleteTokenMap = Record<TokenName, string>;

/** A synthetic fixture whose modes are known-complete at compile time — structurally still a
 * `ThemeManifestFixture` (every gate function below accepts one interchangeably with a
 * manifest loaded off disk), just with a stronger guarantee this file's own test data
 * doesn't need runtime membership checking against itself. */
interface SyntheticThemeManifest {
  readonly slug: string;
  readonly modes: { readonly light: CompleteTokenMap; readonly dark: CompleteTokenMap };
}

/** Every pair here is comfortably >10:1 — a synthetic theme built to pass the gate cleanly,
 * so AC8/AC9 can perturb exactly one value and know any resulting failure is theirs. */
function passingSyntheticManifest(slug: string): SyntheticThemeManifest {
  const light: CompleteTokenMap = {
    bg: "#ffffff",
    surface: "#f5f5f5",
    "surface-2": "#eeeeee",
    line: "#dddddd",
    ink: "#000000",
    mute: "#333333",
    accent: "#202020",
    "accent-ink": "#ffffff",
    "accent-2": "#333333",
    danger: "#202020",
    "danger-ink": "#ffffff",
    success: "#4caf50",
    "sched-1": "#888888",
    "sched-2": "#888888",
    "sched-3": "#888888",
    "sched-4": "#888888",
    "sched-5": "#888888",
    "sched-6": "#888888",
  };
  const dark: CompleteTokenMap = {
    bg: "#000000",
    surface: "#111111",
    "surface-2": "#181818",
    line: "#222222",
    ink: "#ffffff",
    mute: "#cccccc",
    accent: "#eeeeee",
    "accent-ink": "#000000",
    "accent-2": "#cccccc",
    danger: "#eeeeee",
    "danger-ink": "#000000",
    success: "#4caf50",
    "sched-1": "#888888",
    "sched-2": "#888888",
    "sched-3": "#888888",
    "sched-4": "#888888",
    "sched-5": "#888888",
    "sched-6": "#888888",
  };
  return { slug, modes: { light, dark } };
}

describe("Feature: the theme shelf and its contrast gate", () => {
  // ── HAPPY PATH ──────────────────────────────────────────────────────────

  describe("Scenario: the shelf is populated", () => {
    // KNOWN-RED through Ship 1 by ruling — see the header note. Left pending, not weakened.
    it.todo(
      "ships at least six themes (T171, AC1 — expected red until Ship 2)"
    );
  });

  describe("Scenario: every theme is complete", () => {
    it("defines a complete light token set for every shipped theme (T158, AC2)", () => {
      for (const manifest of loadThemeManifests()) {
        assertModeHasFullVocabulary(manifest, "light");
      }
    });

    it("defines a complete dark token set for every shipped theme (T158, AC2)", () => {
      for (const manifest of loadThemeManifests()) {
        assertModeHasFullVocabulary(manifest, "dark");
      }
    });
  });

  describe("Scenario: body text clears AA on every ground", () => {
    it("ink meets 4.5:1 against bg in every theme and mode (T158, AC3)", () => {
      for (const manifest of loadThemeManifests()) {
        for (const mode of MODES) {
          assertPairMeetsAA(manifest, mode, "ink", "bg");
        }
      }
    });

    it("ink meets 4.5:1 against surface in every theme and mode (T158, AC3)", () => {
      for (const manifest of loadThemeManifests()) {
        for (const mode of MODES) {
          assertPairMeetsAA(manifest, mode, "ink", "surface");
        }
      }
    });

    it("ink meets 4.5:1 against surface-2 in every theme and mode (T158, AC3)", () => {
      for (const manifest of loadThemeManifests()) {
        for (const mode of MODES) {
          assertPairMeetsAA(manifest, mode, "ink", "surface-2");
        }
      }
    });
  });

  describe("Scenario: on-accent text clears AA", () => {
    // The pair that forced dark to invert --accent-ink to deep walnut: cream on the lifted
    // dark --accent reaches only ~2.8:1.
    it("accent-ink meets 4.5:1 against accent in every theme and mode (T158, AC4)", () => {
      for (const manifest of loadThemeManifests()) {
        for (const mode of MODES) {
          assertPairMeetsAA(manifest, mode, "accent-ink", "accent");
        }
      }
    });
  });

  describe("Scenario: on-danger text clears AA", () => {
    it("danger-ink meets 4.5:1 against danger in every theme and mode (T158, AC5)", () => {
      for (const manifest of loadThemeManifests()) {
        for (const mode of MODES) {
          assertPairMeetsAA(manifest, mode, "danger-ink", "danger");
        }
      }
    });
  });

  describe("Scenario: secondary text clears AA", () => {
    // --accent-2 is the token this check already caught once, at #8a7b3f.
    it("mute meets 4.5:1 against every ground it renders on, in every theme and mode (T158, AC6)", () => {
      for (const manifest of loadThemeManifests()) {
        for (const mode of MODES) {
          for (const ground of GROUNDS) {
            assertPairMeetsAA(manifest, mode, "mute", ground);
          }
        }
      }
    });

    it("accent-2 meets 4.5:1 against every ground it renders on, in every theme and mode (T158, AC6)", () => {
      for (const manifest of loadThemeManifests()) {
        for (const mode of MODES) {
          for (const ground of GROUNDS) {
            assertPairMeetsAA(manifest, mode, "accent-2", ground);
          }
        }
      }
    });
  });

  describe("Scenario: the gate reads theme data", () => {
    it("iterates the theme manifests rather than parsing CSS declaration blocks (T158, AC7)", () => {
      const files = readdirSync(THEMES_DIR);
      expect(files.length).toBeGreaterThan(0);

      // Every shipped source is a JSON manifest — no `.css` file lives in the themes
      // directory this gate reads, and this file's own source declares no CSS block
      // extractor (that machinery — extractBlock/tokenValue — lives only in
      // design-system-foundation.spec.ts, over globals.css, unrelated to this gate).
      for (const file of files) {
        expect(file.endsWith(".json")).toBe(true);
      }

      const manifests = loadThemeManifests();
      expect(manifests).toHaveLength(files.length);
      for (const manifest of manifests) {
        expect(typeof manifest.slug).toBe("string");
      }
    });
  });

  // ── SAD PATH ────────────────────────────────────────────────────────────

  describe("Scenario: rejecting a theme that fails contrast", () => {
    // A synthetic theme, not a shipped one — cream-enamel passes today, so proving the
    // gate FAILS needs a fixture built to fail. `mute` is set equal to `bg` (1:1).
    const failingManifest = passingSyntheticManifest("gate-test-failing-mute");
    const failingLight = {
      ...failingManifest.modes.light,
      mute: failingManifest.modes.light.bg,
    };
    const failingTheme: ThemeManifestFixture = {
      slug: failingManifest.slug,
      modes: { light: failingLight, dark: failingManifest.modes.dark },
    };

    it("fails when a theme's mute falls below 4.5:1 against one of its grounds (T158, AC8)", () => {
      expect(() => assertPairMeetsAA(failingTheme, "light", "mute", "bg")).toThrow();
    });

    it("names the theme, the mode, the token pair and the measured ratio on failure (T158, AC8)", () => {
      expect(() => assertPairMeetsAA(failingTheme, "light", "mute", "bg")).toThrow(
        /gate-test-failing-mute.*mode 'light'.*'mute' on 'bg'.*1\.00:1/
      );
    });
  });

  describe("Scenario: a new theme cannot skip the gate", () => {
    it("measures a theme added to the shelf with no change to the check itself (T158, AC9)", () => {
      // No code above names "gate-test-new-theme" — assertPairMeetsAA and
      // assertModeHasFullVocabulary are the exact same functions the happy-path specs run
      // against the real shipped manifests, called here with a slug they have never seen.
      const newTheme = passingSyntheticManifest("gate-test-new-theme");

      for (const mode of MODES) {
        expect(() => assertModeHasFullVocabulary(newTheme, mode)).not.toThrow();
        for (const ground of GROUNDS) {
          expect(() => assertPairMeetsAA(newTheme, mode, "ink", ground)).not.toThrow();
          expect(() => assertPairMeetsAA(newTheme, mode, "mute", ground)).not.toThrow();
          expect(() => assertPairMeetsAA(newTheme, mode, "accent-2", ground)).not.toThrow();
        }
        expect(() => assertPairMeetsAA(newTheme, mode, "accent-ink", "accent")).not.toThrow();
        expect(() => assertPairMeetsAA(newTheme, mode, "danger-ink", "danger")).not.toThrow();
      }

      // And the same unmodified gate still catches a deliberate failure on this SAME new
      // theme — nothing here allowlists "gate-test-new-theme" out of being measured.
      const brokenVariant: ThemeManifestFixture = {
        slug: newTheme.slug,
        modes: {
          light: { ...newTheme.modes.light, mute: newTheme.modes.light.bg },
          dark: newTheme.modes.dark,
        },
      };
      expect(() => assertPairMeetsAA(brokenVariant, "light", "mute", "bg")).toThrow(
        /gate-test-new-theme/
      );
    });
  });
});
