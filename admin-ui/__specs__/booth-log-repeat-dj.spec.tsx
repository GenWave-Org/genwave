// @jest-environment jsdom
// T729 — a repeat-caught booth-log row names the DJ who wrote it, resolved from the row's personaId
// through the real fetch -> BoothLogView -> BoothLogFeed wiring (display-only; stored summary untouched).

import { describe, it, expect, jest, beforeEach, afterEach } from "@jest/globals";
import { render, screen, act } from "@testing-library/react";
import "@testing-library/jest-dom/jest-globals";
import { BoothLogView } from "../app/(authed)/booth-log/BoothLogView";

function installFetchMock(personas: unknown, entries: unknown[]): void {
  const fn = jest.fn<typeof fetch>().mockImplementation((input) => {
    const body = String(input).includes("/api/personas") ? personas : { entries, nextBefore: null };
    return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve(body) } as Response);
  });
  global.fetch = fn as unknown as typeof fetch;
}

async function flush(): Promise<void> {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(0);
  });
}

beforeEach(() => {
  jest.useFakeTimers({ now: new Date("2026-01-01T12:00:00.000Z") });
});

afterEach(() => {
  jest.useRealTimers();
  jest.restoreAllMocks();
});

describe("Feature: booth-log repeat-caught rows name their DJ", () => {
  it("reads 'Nova wrote ... again — cut short' for a row stamped with Nova's persona id", async () => {
    installFetchMock(
      [{ id: 7, name: "Nova" }],
      [
        {
          occurredAt: "2026-01-01T10:04:00.000Z",
          kind: "repeat-caught",
          summary: 'Repeat caught: "banger, log it" — trimmed',
          id: 601,
          personaId: 7,
        },
      ]
    );

    render(<BoothLogView timeZone="UTC" />);
    await flush();

    expect(screen.getByText('Nova wrote "banger, log it" again — cut short')).toBeInTheDocument();
  });
});
