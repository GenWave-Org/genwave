// @jest-environment jsdom
// PLAN T740 — Memory tab rows say asked and aired (SPEC F218.48, STORY-552 AC2, AC3)
// Runner: Jest (jsdom). Pending at plan time (2026-11-16): T740 turns each it.todo into an it.

import { describe, it } from "@jest/globals";

describe("STORY-552 — asked and aired on the Memory tab", () => {
  it.todo("AC2 — reads Asked 4 times · Aired once");
  it.todo("AC3 — reads Not asked yet for a memory with no offers");
});
