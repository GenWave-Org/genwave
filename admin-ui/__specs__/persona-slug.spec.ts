// Parity guard (review finding, PLAN T68): `personaSlug` hand-mirrors the backend's
// `LegacyPersonaCardMapper.Slugify` (SPEC F71.1) with no shared source of truth — drift here would
// silently 404 an export link or upsert an import onto the WRONG persona row. `persona-slug
// .parity-cases.ts` is the one authored case table; the C# half (`FeaturePersonaSlugParity`,
// tests/GenWave.MediaLibrary.Tests/Specs/Story192_PersonaSlugParity.cs) string-parses that SAME
// file and runs the real `LegacyPersonaCardMapper.Slugify` against it, so a change to either
// implementation that drifts from this table fails a spec on THAT toolchain.
//
// Runner: Jest (node) — pure string logic, no DOM needed.

import { describe, it, expect } from "@jest/globals";
import { personaSlug } from "../app/(authed)/personas/persona-slug";
import { PERSONA_SLUG_PARITY_CASES } from "./persona-slug.parity-cases";

describe("Feature: personaSlug stays byte-for-byte parity with the backend's Slugify", () => {
  describe("Scenario: shared parity cases, pinned two ways (mirrored by FeaturePersonaSlugParity)", () => {
    for (const [name, expected] of PERSONA_SLUG_PARITY_CASES) {
      it(`personaSlug(${JSON.stringify(name)}) === ${JSON.stringify(expected)}`, () => {
        expect(personaSlug(name)).toBe(expected);
      });
    }
  });

  describe("Scenario: the empty-result fallback token matches the C# sentinel verbatim", () => {
    it('falls back to the literal "persona" — not "Persona", "unknown", or empty', () => {
      expect(personaSlug("!!!")).toBe("persona");
    });
  });
});
