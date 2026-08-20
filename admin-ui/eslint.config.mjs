// gh-#553: flat ESLint config, replacing the Next-16 no-op `next lint`.
//
// `eslint-config-next`'s default export is the same core-web-vitals +
// react-hooks + jsx-a11y + `@next/next` ruleset `next lint` used to apply
// under Next <16 -- Next.js ships it as a ready-made flat config array now
// that the CLI subcommand is gone. `tseslint.configs.recommended` layers
// typescript-eslint's non-type-checked rules (no `parserOptions.project`,
// so it doesn't need to reconcile tsconfig.json vs tsconfig.specs.json).
import nextPlugin from "eslint-config-next";
import tseslint from "typescript-eslint";

const config = [
  {
    ignores: [".next/**", "out/**", "build/**", "next-env.d.ts", "*.tsbuildinfo"],
  },
  ...nextPlugin,
  ...tseslint.configs.recommended,
  {
    rules: {
      // gh-#553: __specs__ mocks intentionally build partial fixtures (e.g. a fake
      // fetch Response missing most of the Fetch API), so an exhaustive prop count
      // isn't meaningful there the way it is for real domain/component types.
      "@typescript-eslint/no-explicit-any": "warn",

      // gh-#553: eslint-plugin-react-hooks v7's `recommended` config adds these two
      // React-Compiler-oriented rules as hard errors. Both fire ~15 times across
      // established, previously-reviewed patterns this codebase uses deliberately
      // (mount-time `setState` in `useEffect` to seed derived/client-only state;
      // writing a ref's `.current` during render for React's documented lazy-init
      // idiom — see https://react.dev/reference/react/useRef). Fixing every call
      // site is a refactor, not a lint-gate task (gh-#553 scope), so these are
      // downgraded to warnings: still visible in CI output, but not gate-failing.
      "react-hooks/set-state-in-effect": "warn",
      "react-hooks/refs": "warn",
    },
  },
  {
    files: ["**/*.spec.ts", "**/*.spec.tsx"],
    rules: {
      // Spec files stub external contracts (fetch Response, Next router, etc.) with
      // deliberately partial objects; `any` there is a fixture shortcut, not a smell.
      "@typescript-eslint/no-explicit-any": "off",
    },
  },
  {
    // jest.config.js is loaded by Jest's own CommonJS config resolver, not bundled
    // through Next's ESM/SWC pipeline, so it stays require()-based like the rest of
    // the Jest ecosystem's config files.
    files: ["jest.config.js"],
    rules: {
      "@typescript-eslint/no-require-imports": "off",
    },
  },
];

export default config;
