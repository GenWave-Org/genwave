// @jest-environment jsdom
// STORY-218 — The Live card tells me why this track is playing right now (SPEC F86.4 — T76)
//
// Runner: Jest + jsdom + @testing-library/react. The now-playing card renders the SAME
// PickChips component as the booth log, fed from the same stamped booth-log row the taste
// thumbs already target (T71 correlation) — no separate now-playing diagnostics channel.
// An unstamped airing shows no chips and no badge; the card is otherwise unchanged.
//
// Specs are it.todo pending until T76 ships (depends on T75's PickChips extraction).
// Un-pin against LiveView's now-playing card once the row payload plumbing lands.

import { describe, it } from "@jest/globals";

describe("Feature: live now-playing why-this-pick chips", () => {
  describe("Scenario: current airing with a stamped pick", () => {
    it.todo("renders the airing's fired-rule chips on the now-playing card");
    it.todo("renders the exploration badge for a stamped exploration airing");
    it.todo("sources chips from the same booth-log row the thumbs target");
  });

  describe("Scenario: current airing without a stamped pick", () => {
    it.todo("renders no chips and no badge");
    it.todo("leaves the rest of the card unchanged");
  });
});
