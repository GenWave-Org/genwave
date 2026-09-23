import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { apiGet } from "@/lib/api";
import { isAboutResponseDto } from "@/lib/about-api";
import { isUnauthorizedStatus, SESSION_EXPIRED_PATH } from "@/lib/api-fetch";
import { AboutView } from "./AboutView";

// The build/station/library/uptime/attribution facts (SPEC F207.1, F207.2; STORY-474; PLAN T561)
// — always re-render from the server: uptime and library count change on their own even when
// nothing here was edited, so this route can never be statically cached (settings/page.tsx's own
// posture).
export const dynamic = "force-dynamic";
export const fetchCache = "force-no-store";

const PAGE_TITLE = <h1 className="font-display text-[1.35rem] font-semibold text-ink">About</h1>;

/** The one shape every sad path on this page renders — only the message differs. */
function ErrorPage({ message }: { message: string }): ReactNode {
  return (
    <main>
      {PAGE_TITLE}
      <p className="mt-2 text-[0.85rem] text-mute">{message}</p>
    </main>
  );
}

export default async function AboutPage(): Promise<ReactNode> {
  const cookieStore = await cookies();
  const cookieStr = cookieStore.toString();

  const response = await apiGet("/api/about", { cookies: cookieStr });

  // A stale cookie: one owner for the 401 -> sign-out contract (SPEC F208.1, STORY-475). A
  // Server Component render can't clear the cookie itself, so this hands off to a route handler
  // that can (app/session-expired/route.ts).
  if (isUnauthorizedStatus(response.status)) {
    redirect(SESSION_EXPIRED_PATH);
  }

  if (response.status === 403) {
    return <ErrorPage message="You do not have permission to view this page." />;
  }

  if (!response.ok) {
    return <ErrorPage message="Unable to load this page." />;
  }

  const raw: unknown = await response.json();

  if (!isAboutResponseDto(raw)) {
    return <ErrorPage message="Unable to load this page." />;
  }

  return (
    <main>
      {PAGE_TITLE}
      <AboutView about={raw} />
    </main>
  );
}
