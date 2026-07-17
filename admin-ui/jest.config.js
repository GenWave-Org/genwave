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
