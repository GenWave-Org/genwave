// STORY-281 — Packs on the shelf + the honest specimen (SPEC F104.3, F104.4 · PLAN T201/T202)
describe("Feature: packs on the shelf with an honest specimen", () => {
  describe("Scenario: the shelf card is meta-only", () => {
    it.todo("renders family, description, and byte total from meta alone (T201, AC1)");
    it.todo("issues no asset fetch on browse (T201, AC1)");
  });
  describe("Scenario: the specimen is the real face", () => {
    it.todo("renders the specimen in the pack's hash-verified face (T202, AC2)");
    it.todo("discards everything on close — nothing installed, nothing station-wide (T202, AC2)");
  });
  describe("Scenario: an unreachable asset degrades", () => {
    it.todo("shows degraded copy without crashing on an integrity/connectivity failure (T202, AC3)");
  });
});
