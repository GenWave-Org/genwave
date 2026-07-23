// STORY-220 — The mood filter's choices stay byte-for-byte parity with the backend (SPEC F86.8,
// T80). moodVocabulary.ts hand-mirrors GenWave.Core.Domain.MoodVocabulary.Terms with no facet
// fetch (a fixed vocabulary needs no discovery) — this spec is the one-sided drift guard: it
// parses the REAL C# source file straight out of the repo and asserts the mirrored TS constant
// against it. Runs in the repo, so CI catches drift the moment either list changes.
//
// Runner: Jest (node) — pure string/array logic, no DOM needed. Mirrors persona-slug.spec.ts's
// parity idiom (T68's FeaturePersonaSlugParity precedent), run in reverse: there the C# theory
// string-parses a TS file; here the TS spec string-parses a C# file, since MoodVocabulary.cs
// (not a TS constant) is the canonical source (SPEC F85.1).

import { describe, it, expect } from "@jest/globals";
import { readFileSync } from "node:fs";
import path from "node:path";
import { MOOD_VOCABULARY } from "../app/(authed)/catalog/moodVocabulary";

const MOOD_VOCABULARY_CS_PATH = path.resolve(
  __dirname,
  "../../src/GenWave.Abstractions/Domain/MoodVocabulary.cs"
);

/** Pulls the `Terms` array literal's quoted string entries out of MoodVocabulary.cs's source
 * text, in source order — no C# parser, just the same string-literal extraction idiom the C#
 * side's `FeaturePersonaSlugParity` uses on persona-slug.parity-cases.ts, in reverse. */
function parseCSharpMoodTerms(source: string): string[] {
  const arrayMatch = source.match(/Terms\s*=\s*\[([\s\S]*?)\];/);
  if (arrayMatch === null) {
    throw new Error("MoodVocabulary.cs: could not locate the `Terms = [...]` array literal.");
  }
  const [, arrayBody] = arrayMatch;
  const terms: string[] = [];
  for (const entryMatch of arrayBody.matchAll(/"([a-z]+)"/g)) {
    const [, term] = entryMatch;
    if (term !== undefined) terms.push(term);
  }
  return terms;
}

describe("Feature: the admin UI's mood vocabulary stays byte-for-byte parity with the backend's MoodVocabulary (F86.8)", () => {
  describe("Scenario: the mirrored TS constant is read against the real C# source file", () => {
    it("MOOD_VOCABULARY equals MoodVocabulary.Terms parsed straight out of MoodVocabulary.cs", () => {
      const csSource = readFileSync(MOOD_VOCABULARY_CS_PATH, "utf-8");
      expect(MOOD_VOCABULARY).toEqual(parseCSharpMoodTerms(csSource));
    });
  });
});
