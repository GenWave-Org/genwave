// gh-#168: pin timezone + locale for the whole suite BEFORE any worker spawns. Specs pin literal
// rendered dates ("Imported · file · Jul 20, 2026") whose field ORDER is locale-dependent (en_GB
// renders "20 Jul 2026"), so an unpinned suite false-positives on a differently-configured dev
// box (probe-verified: personas-page fails 2 facts under LANG=en_GB without this). Test workers
// are child processes that inherit this env at THEIR startup — the moment Node fixes both the ICU
// default locale and the Date timezone — so a full `npm test` run is pinned regardless of the
// box (probe-verified green under LANG=en_GB + TZ=Australia/Sydney). Known boundary: a
// SINGLE-file `npx jest foo` run may execute in-band in this already-started main process, whose
// ICU locale cannot change post-startup — locale-literal specs can still flap there on an exotic
// box; the full suite and CI never do. Production is untouched: the operator's own locale/TZ
// stays correct and desirable.
process.env.TZ = "UTC";
process.env.LANG = "en_US.UTF-8";
process.env.LC_ALL = "en_US.UTF-8";

/** @type {import('jest').Config} */
const nextJest = require("next/jest.js");

const createJestConfig = nextJest({ dir: "./" });

// Shared base — next/jest injects transform, moduleNameMapper for CSS/images,
// and other Next.js-specific settings on top of whatever we pass here.

/** @type {import('jest').Config} */
const nodeConfig = {
  displayName: "node",
  testEnvironment: "node",
  testMatch: ["**/__specs__/**/*.spec.ts"],
  moduleNameMapper: { "^@/(.*)$": "<rootDir>/$1" },
  modulePathIgnorePatterns: ["<rootDir>/.next/"],
};

/** @type {import('jest').Config} */
const jsdomConfig = {
  displayName: "jsdom",
  testEnvironment: "jest-environment-jsdom",
  testMatch: ["**/__specs__/**/*.spec.tsx"],
  moduleNameMapper: { "^@/(.*)$": "<rootDir>/$1" },
  modulePathIgnorePatterns: ["<rootDir>/.next/"],
  // jsdom-only: purges sonner's module-global toast store between tests (gh-#516).
  setupFilesAfterEnv: ["<rootDir>/jest.setup.ts"],
};

// createJestConfig wraps an async function; Jest supports async config exports.
// We build each project config through the same next/jest pipeline so that
// the SWC transformer and Next.js module aliases are applied to both.
module.exports = async () => {
  const [resolvedNode, resolvedJsdom] = await Promise.all([
    createJestConfig(nodeConfig)(),
    createJestConfig(jsdomConfig)(),
  ]);
  return {
    projects: [resolvedNode, resolvedJsdom],
  };
};
