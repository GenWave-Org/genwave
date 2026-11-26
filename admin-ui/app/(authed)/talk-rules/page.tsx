import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { apiGet } from "@/lib/api";
import { isUnauthorizedStatus, SESSION_EXPIRED_PATH } from "@/lib/api-fetch";
import { narrowTalkRules, talkRulesModeFrom, type TalkRulesMode } from "@/lib/talk-rules-api";
import { TalkRulesClient } from "./TalkRulesClient";

// The rule list, switches and counts change underneath this page — always re-render from the server.
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

const TITLE = <h1 className="font-display text-[1.35rem] font-semibold text-ink">Talk rules</h1>;

async function readMode(cookieHeader: string): Promise<TalkRulesMode | null> {
  try {
    const response = await apiGet("/api/settings", { cookies: cookieHeader });
    if (!response.ok) return null;
    return talkRulesModeFrom((await response.json()) as unknown);
  } catch {
    return null;
  }
}

export default async function TalkRulesPage(): Promise<ReactNode> {
  const cookieHeader = (await cookies()).toString();
  const [rulesResponse, mode] = await Promise.all([
    apiGet("/api/talk-rules", { cookies: cookieHeader }),
    readMode(cookieHeader),
  ]);

  if (isUnauthorizedStatus(rulesResponse.status)) {
    redirect(SESSION_EXPIRED_PATH);
  }

  const rules = rulesResponse.ok ? narrowTalkRules((await rulesResponse.json()) as unknown) : null;
  if (rules === null) {
    return (
      <main>
        {TITLE}
        <p className="mt-4 text-[0.85rem] text-danger">Unable to load the talk rules.</p>
      </main>
    );
  }

  return (
    <main>
      {TITLE}
      <div className="mt-4">
        <TalkRulesClient initialRules={rules} mode={mode} />
      </div>
    </main>
  );
}
