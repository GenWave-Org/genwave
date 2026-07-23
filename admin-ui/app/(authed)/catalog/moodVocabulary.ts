/**
 * Mirror of the backend's fixed mood vocabulary (SPEC F85.1, F86.8) —
 * `GenWave.Core.Domain.MoodVocabulary.Terms` (src/GenWave.Abstractions/Domain/MoodVocabulary.cs).
 *
 * The mood filter (SPEC F86.8) offers exactly these terms and issues NO facet/discovery request:
 * a fixed vocabulary needs no discovery. Kept byte-for-byte in step with the C# source by
 * `catalog-mood-vocabulary-parity.spec.ts`, which parses `MoodVocabulary.cs` straight out of the
 * repo and asserts this array against it — the same "read the other language's file in the spec"
 * idiom `FeaturePersonaSlugParity` uses in reverse (T68).
 *
 * A future vocabulary version only ever APPENDS terms (see the C# doc comment) — this array grows
 * the same way, never reorders or removes an entry.
 */
export const MOOD_VOCABULARY: readonly string[] = [
  "dreamy", "driving", "somber", "playful", "tense", "warm", "cold", "epic",
  "intimate", "gritty", "breezy", "brooding", "triumphant", "wistful", "hypnotic", "raucous",
];
