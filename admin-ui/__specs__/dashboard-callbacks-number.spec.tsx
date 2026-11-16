// @jest-environment jsdom
// PLAN T738 — "Callbacks per DJ-hour" on the Status page (SPEC F218.47, STORY-551 AC2, AC3)
// Runner: Jest (jsdom). Pending at plan time (2026-11-16): T738 turns each it.todo into an it.

import { describe, it } from "@jest/globals";

describe("STORY-551 — the callback count on the Status page", () => {
  it.todo("AC2 — shows Callbacks per DJ-hour as 0.75");
  it.todo("AC3 — reads None yet with no offers");
});
