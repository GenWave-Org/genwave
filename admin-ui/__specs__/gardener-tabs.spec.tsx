// STORY-381 — I browse the queue one kind at a time (SPEC F153.10 rider 2026-08-31 · PLAN T387 · gh-#657)
//
// BDD specification — Jest, pending (it.todo) until T387 rebuilds the Gardener page on the
// catalog idiom (server-rendered, URL-driven ?tab=&page=&limit=). /build-loop turns each todo
// into a real spec: render the page (or its extracted pure helpers/client children) with the
// scenario's searchParams and fetch fakes, one assertion per spec. The full production arc
// (real binary + browser) is T387's own acceptance smoke — these specs pin the component/helper
// behavior the way catalog-facet-pickers/gardener-page already do for their pages.
//
// gh-#655 rides this story (AC6): the purge trigger's verb-object label is pinned here.

describe("Feature: Gardener kind tabs", () => {
  describe("Scenario: five tabs, badged from status", () => {
    it.todo("renders five tabs in the fixed kind order");
    it.todo("labels each tab with its kind's open count from /api/status");
  });

  describe("Scenario: a tab shows only its own kind", () => {
    it.todo("fetches the active tab's kind with kind=<tab>&state=open");
    it.todo("renders only the active kind's rows");
  });

  describe("Scenario: the URL owns the active tab", () => {
    it.todo("activates the tab named by ?tab=");
    it.todo("renders each tab as a link to its own ?tab= URL");
  });

  describe("Scenario: an empty kind names itself", () => {
    it.todo("renders the kind's own empty state when it has zero open findings");
    it.todo("keeps the empty kind's badge at 0");
  });

  describe("Scenario: purge lives on the dead-files tab", () => {
    it.todo("renders the purge action in the dead-files tab header");
    it.todo("gives the purge trigger a verb-object label, never a status reading (gh-#655)");
    it.todo("renders no purge action on any other tab");
  });

  describe("Scenario: verbs refresh the page", () => {
    it.todo("refreshes via router.refresh after a dismiss completes");
  });

  // ── Sad path ──────────────────────────────────────────────────────────
  describe("Scenario: default and unknown tab fall back silently", () => {
    it.todo("activates the first tab in kind order when ?tab= is absent");
    it.todo("activates the first tab in kind order when ?tab= is unrecognized, with no error");
  });
});
