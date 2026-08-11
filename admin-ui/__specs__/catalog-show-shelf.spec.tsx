// STORY-315 — Hire a show from the shelf (F118.2, F118.3) — shelf/modal half — PENDING
// scaffold (T255, planned 2026-08-10). The import endpoint half is xUnit
// (Host.Tests/Story315_ShowImport.cs).

describe("Feature: The show shelf", () => {
  describe("Scenario: browsing show cards", () => {
    it.todo("show cards render name, tagline, and bestFor chips beside personas/themes/fonts");
    it.todo("the detail modal shows the FULL card including flavor before confirm (F90 trust posture)");
  });

  describe("Scenario: the soft hire offer", () => {
    it.todo("offers 'also hire' only when the suggested persona is on the shelf and not hired");
    it.todo("declining the offer imports the show and hires nothing");
    it.todo("an absent or unknown suggestion renders no offer and no error");
  });
});
