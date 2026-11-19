// @jest-environment jsdom
// T753b round 2 (STORY-556, SPEC F219.8) — a voice setting value that is an engine map reads plainly wherever
// the UI shows a voice, and stays selectable in the voice dropdown.

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, waitFor } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { voiceSettingLabel } from "@/lib/voice-setting-label";
import { VoiceControl } from "../app/(authed)/safe-content/VoiceControl";

const MAP = '{"kokoro":"af_nova","chatterbox":"nova"}';

describe("voiceSettingLabel", () => {
  it("reads an engine map as one voice per engine", () => {
    expect(voiceSettingLabel(MAP)).toBe("One voice per engine (chatterbox: nova, kokoro: af_nova)");
  });

  it("leaves a bare voice id as it is", () => {
    expect(voiceSettingLabel("af_bella")).toBe("af_bella");
  });

  it("leaves malformed JSON as it is", () => {
    expect(voiceSettingLabel('{"kokoro":')).toBe('{"kokoro":');
  });

  it("leaves a map with a non-text voice as it is", () => {
    expect(voiceSettingLabel('{"kokoro":3}')).toBe('{"kokoro":3}');
  });
});

describe("VoiceControl with a saved engine map", () => {
  beforeEach(() => {
    global.fetch = jest.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ["af_bella", "am_michael"],
    })) as unknown as typeof fetch;
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it("offers the map as a plainly labelled option", async () => {
    render(<VoiceControl id="v" value={MAP} onChange={() => {}} disabled={false} />);

    await waitFor(() =>
      expect(
        screen.getByRole("option", { name: "One voice per engine (chatterbox: nova, kokoro: af_nova)" })
      ).toBeInTheDocument()
    );
  });

  it("selects the map option", async () => {
    render(<VoiceControl id="v" value={MAP} onChange={() => {}} disabled={false} />);

    await waitFor(() => expect(screen.getByRole("combobox")).toHaveValue(MAP));
  });
});
