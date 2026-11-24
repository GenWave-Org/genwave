// @jest-environment jsdom
// STORY-572 — The Talk rules page (SPEC F220.27 · PLAN T804)
//
// BDD specification — Jest. Pending: every specification is it.todo — turn it into an `it` only in the
// task that makes it green (T804). Each Given comment names the arrange the scenario needs.

import { describe, it } from "@jest/globals";

describe("Feature: The Talk rules page", () => {
  // Given the rules API returns two rules, one covering DJ lines and chats, one covering ads only
  describe("Scenario: the rules are listed", () => {
    it.todo("shows each rule's name"); // AC1
    it.todo("shows what each rule does"); // AC1
    it.todo("shows one switch per kind of talk the rule can cover"); // AC2
    it.todo("shows how many lines each rule flagged in the last 7 days"); // AC4
  });

  // Given a rule switched on for chats; When its chat switch is clicked
  describe("Scenario: a switch is clicked", () => {
    it.todo("sends the PUT for that rule and kind"); // AC3
    it.todo("shows the switch in its new state"); // AC3
  });

  // Given a rule with flagged lines; When the rule is opened
  describe("Scenario: a rule is opened", () => {
    it.todo("shows the DJ of each flagged line"); // AC5
    it.todo("shows the words of each flagged line"); // AC5
    it.todo("shows the outcome of each flagged line"); // AC5
  });

  // When "Check against last week" is clicked
  describe("Scenario: checking against last week", () => {
    it.todo("shows each rule's replay count"); // AC6
  });

  // Given mode shadow or legacy
  describe("Scenario: today's checks still decide", () => {
    it.todo("says today's checks still decide what airs in shadow"); // AC7
    it.todo("says today's checks still decide what airs in legacy"); // AC7
    it.todo("shows no such note in mode rules"); // AC7
  });

  // Sad path — Given the PUT fails
  describe("Scenario: saving a switch fails", () => {
    it.todo("returns the switch to its old state"); // AC8
    it.todo("shows an error"); // AC8
  });

  // Sad path — Given a rule that can't cover DJ lines
  describe("Scenario: a kind the rule can't cover", () => {
    it.todo("shows no switch for that kind"); // AC9
  });
});
