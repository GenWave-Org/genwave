import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import type { LibraryDto } from "@/lib/library";
import { PronunciationRulesControl } from "./PronunciationRulesControl";
import { SettingsForm } from "./SettingsForm";
import type { SettingDto } from "./SettingsForm";

// Settings are mutable (operators write via the API); always re-render from the server.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

interface SettingsPageProps {
  // no dynamic segments
}

export default async function SettingsPage(
  _props: SettingsPageProps
): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieStr = cookieStore.toString();

  const [settingsResp, librariesResp] = await Promise.all([
    apiGet("/api/settings", { cookies: cookieStr }),
    apiGet("/api/libraries", { cookies: cookieStr }),
  ]);

  if (settingsResp.status === 403) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Settings</h1>
        <p className="mt-2 text-[0.85rem] text-mute">You do not have permission to view settings.</p>
      </main>
    );
  }

  if (!settingsResp.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Settings</h1>
        <p className="mt-2 text-[0.85rem] text-mute">Unable to load settings.</p>
      </main>
    );
  }

  const settings = (await settingsResp.json()) as SettingDto[];

  // Libraries are used to populate the SafeScope picker; fall back to empty list
  // if the libraries endpoint is unavailable so the rest of the settings page still renders.
  let libraries: LibraryDto[] = [];
  if (librariesResp.ok) {
    libraries = (await librariesResp.json()) as LibraryDto[];
  }

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Settings</h1>
      <div className="mt-4">
        {/* T145 (SPEC F97, F100.3, STORY-254, review F3) — PronunciationRulesControl is a
            dedicated-API surface (reads a merged station∪persona view no settings key carries;
            writes immediately, never the page-wide Save batch), so it can't register in
            SettingsForm's per-key SETTING_CONTROL_REGISTRY. Threaded in via `ttsTabExtra` rather
            than SettingsForm importing it directly: it lands inside the TTS tabpanel (AC1's "TTS
            surface", beside the raw Tts:Pronunciations field), while every other SettingsForm
            consumer — including its own jsdom spec suite — stays unaware this component exists. */}
        <SettingsForm
          settings={settings}
          libraries={libraries}
          ttsTabExtra={<PronunciationRulesControl />}
        />
      </div>
    </main>
  );
}
