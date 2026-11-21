// @jest-environment jsdom
// PLAN T758 — the Voices page (SPEC F219.12–F219.14, STORY-557 AC6)
// Runner: Jest (jsdom). Drives VoicesClient with a fetch mock dispatched by "METHOD url".

jest.mock("next/headers", () => ({
  cookies: jest.fn<() => Promise<{ toString: () => string }>>().mockResolvedValue({ toString: () => "session=test-cookie" }),
}));

import { describe, it, expect, beforeAll, beforeEach, afterAll, jest } from "@jest/globals";
import { render, screen, fireEvent, within, waitFor, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";
import { Toaster } from "@/components/ui/toast";
import { VoicesClient } from "../app/(authed)/voices/VoicesClient";
import type { VoiceClipDto } from "@/lib/voice-clips-api";

const STARTER: VoiceClipDto = {
  slug: "starter-alice",
  label: "Alice",
  durationMs: 12_300,
  source: "starter",
  credit: "LibriTTS-R, CC BY 4.0, https://www.openslr.org/141/",
  createdAt: "2026-11-18T10:00:00Z",
};
const MINE: VoiceClipDto = {
  slug: "mine",
  label: "My voice",
  durationMs: 8_000,
  source: "upload",
  credit: null,
  createdAt: "2026-11-18T11:00:00Z",
};

interface Reply {
  status: number;
  body?: unknown;
}

let mockFetch: jest.MockedFunction<typeof fetch>;
const realFetch = global.fetch;

function installFetch(reply: Reply): void {
  mockFetch = jest.fn<typeof fetch>().mockImplementation(async () => {
    return {
      ok: reply.status >= 200 && reply.status < 300,
      status: reply.status,
      json: () => Promise.resolve(reply.body ?? {}),
    } as unknown as Response;
  });
  global.fetch = mockFetch;
}

function renderPage(clips: VoiceClipDto[]): void {
  render(
    <ConfirmDialogProvider>
      <VoicesClient initialClips={clips} />
      <Toaster />
    </ConfirmDialogProvider>
  );
}

function chooseFile(name = "clip.wav", size = 1024): void {
  const file = new File([new Uint8Array(size)], name, { type: "audio/wav" });
  fireEvent.change(screen.getByLabelText(/^Recording/), { target: { files: [file] } });
}

async function uploadWithLabel(label: string): Promise<void> {
  fireEvent.change(screen.getByLabelText("Label"), { target: { value: label } });
  chooseFile();
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: "Upload clip" }));
    await Promise.resolve();
  });
}

async function confirmDeleteOf(label: string): Promise<void> {
  fireEvent.click(screen.getByRole("button", { name: `Delete ${label}` }));
  const dialog = await screen.findByRole("dialog");
  await act(async () => {
    fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
    await Promise.resolve();
  });
}

afterAll(() => {
  global.fetch = realFetch;
});

describe("Feature: the Voices page", () => {
  describe("Scenario: clips exist", () => {
    describe("When the page renders", () => {
      beforeEach(() => {
        renderPage([STARTER, MINE]);
      });
      it("lists each clip with its label", () => {
        expect(screen.getAllByTestId("clip-label").map((el) => el.textContent)).toEqual(["Alice", "My voice"]);
      });
      it("shows the credit on a starter clip", () => {
        expect(screen.getByTestId("clip-credit")).toHaveTextContent("LibriTTS-R, CC BY 4.0");
      });
    });
  });

  describe("Scenario: an upload", () => {
    beforeAll(async () => {
      installFetch({ status: 201, body: MINE });
      renderPage([]);
      await uploadWithLabel("My voice");
      await waitFor(() => expect(mockFetch).toHaveBeenCalledTimes(1));
    });
    it("uploads a file with a label", () => {
      const form = (mockFetch.mock.calls[0]?.[1]?.body ?? null) as FormData | null;
      expect(form?.get("label")).toBe("My voice");
    });
  });

  describe("Scenario: a delete", () => {
    beforeAll(async () => {
      installFetch({ status: 204 });
      renderPage([MINE]);
      await confirmDeleteOf("My voice");
    });
    it("removes a deleted clip from the list", async () => {
      await waitFor(() => expect(screen.queryByTestId("clip-label")).not.toBeInTheDocument());
    });
  });

  describe("Scenario: the clip list cannot be loaded", () => {
    beforeAll(async () => {
      installFetch({ status: 500 });
      const { default: VoicesPage } = await import("../app/(authed)/voices/page");
      render(await VoicesPage());
    });
    it("says the clips could not be loaded instead of showing an empty list", () => {
      expect(screen.getByText("Unable to load voice clips.")).toBeInTheDocument();
    });
  });

  describe("Scenario: refusals", () => {
    describe("When an upload is refused", () => {
      beforeAll(async () => {
        installFetch({ status: 400, body: { detail: "The clip is shorter than 5 seconds." } });
        renderPage([]);
        await uploadWithLabel("Too short");
      });
      it("shows the reason when an upload is refused", async () => {
        expect(await screen.findByRole("alert")).toHaveTextContent("The clip is shorter than 5 seconds.");
      });
    });

    describe("When a clip in use is deleted", () => {
      beforeAll(async () => {
        installFetch({
          status: 409,
          body: {
            detail: "This clip is still the voice of Nova. Choose another voice for them first.",
            referencedBy: ["Nova"],
          },
        });
        renderPage([MINE]);
        await confirmDeleteOf("My voice");
      });
      it("names the DJs when a clip in use can't be deleted", async () => {
        expect(await screen.findByText(/still the voice of Nova/)).toBeInTheDocument();
      });
    });
  });
});
