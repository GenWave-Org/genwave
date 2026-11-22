import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import type { VoiceClipDto } from "@/lib/voice-clips-api";
import { VoicesClient } from "./VoicesClient";

// Clips change on upload/delete and the starter set is seeded at boot — always render fresh.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

export default async function VoicesPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const [result] = await Promise.allSettled([apiGet("/api/voices/clips", { cookies: cookieStore.toString() })]);
  if (result.status === "rejected" || !result.value.ok) {
    return (
      <main>
        <h1 className="font-display text-[1.35rem] font-semibold text-ink">Voices</h1>
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load voice clips.</p>
      </main>
    );
  }
  const initialClips = (await result.value.json()) as VoiceClipDto[];

  return (
    <main>
      <h1 className="font-display text-[1.35rem] font-semibold text-ink">Voices</h1>
      <p className="mt-1 text-[0.85rem] text-mute">
        A clip is a 5 to 20 second recording of one person talking. A voice engine that copies voices
        listens to it and speaks in that voice.
      </p>
      <div className="mt-4">
        <VoicesClient initialClips={initialClips} />
      </div>
    </main>
  );
}
