// @jest-environment jsdom
// STORY-246 — The roster replaces the switch (SPEC F94.1, PLAN T127)
//
// BDD specification — Jest, pending (it.todo). Section derivation and badge rendering are
// component-testable via the personas-page harness idiom; the "no activation control
// anywhere" sweep and live section updates are T127 browser acceptance (T92 precedent).

describe("Feature: The roster replaces the switch", () => {
  describe("Scenario: scheduled vs bench, derived from schedule data", () => {
    it.todo("groups personas with schedule rows under Scheduled");
    it.todo("groups personas without schedule rows under Bench");
    it.todo("shows the On The Air badge on the current DJ only");
  });

  describe("Scenario: the switch is gone", () => {
    it.todo("renders no Activate/Deactivate control on the Roster page");
    it.todo("renders no persona activation control on the Settings page");
    // Full-app sweep for activation controls = T127 browser acceptance.
  });
});
