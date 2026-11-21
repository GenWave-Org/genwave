// PLAN T754 — the voice setting value <-> per-engine map conversion behind the voice pickers (SPEC F219.8, F219.10)

import { describe, it, expect } from "@jest/globals";
import {
  parseCastSettingValue,
  parseVoiceSettingValue,
  serializeCastSettingValue,
  serializeVoiceSettingValue,
} from "../lib/engine-voice-map";

describe("Feature: a voice setting value as a per-engine map", () => {
  describe("Scenario: reading", () => {
    it("reads a bare id as a kokoro voice", () => {
      expect(parseVoiceSettingValue("af_nova")).toEqual({ kokoro: "af_nova" });
    });
    it("reads a JSON map", () => {
      expect(parseVoiceSettingValue('{"kokoro":"af_nova","chatterbox":"nova"}')).toEqual({
        kokoro: "af_nova",
        chatterbox: "nova",
      });
    });
    it("reads malformed JSON as no choice", () => {
      expect(parseVoiceSettingValue("{oops")).toEqual({});
    });
  });

  describe("Scenario: writing", () => {
    it("writes kokoro alone as a bare id", () => {
      expect(serializeVoiceSettingValue({ kokoro: "af_nova" })).toBe("af_nova");
    });
    it("writes two engines as a JSON map", () => {
      expect(serializeVoiceSettingValue({ kokoro: "af_nova", chatterbox: "nova" })).toBe(
        '{"kokoro":"af_nova","chatterbox":"nova"}'
      );
    });
    it("writes nothing chosen as blank", () => {
      expect(serializeVoiceSettingValue({})).toBe("");
    });
  });

  describe("Scenario: the cast setting", () => {
    it("reads the old comma list as kokoro speakers", () => {
      expect(parseCastSettingValue("af_nova, af_sky")).toEqual([{ kokoro: "af_nova" }, { kokoro: "af_sky" }]);
    });
    it("reads a JSON array of bare ids and maps", () => {
      expect(parseCastSettingValue('["af_nova",{"chatterbox":"nova"}]')).toEqual([
        { kokoro: "af_nova" },
        { chatterbox: "nova" },
      ]);
    });
    it("writes kokoro-only speakers as bare ids and the rest as maps", () => {
      expect(serializeCastSettingValue([{ kokoro: "af_nova" }, { kokoro: "af_sky", chatterbox: "nova" }, {}])).toBe(
        '["af_nova",{"kokoro":"af_sky","chatterbox":"nova"}]'
      );
    });
    it("writes an empty cast as blank", () => {
      expect(serializeCastSettingValue([])).toBe("");
    });
  });
});
