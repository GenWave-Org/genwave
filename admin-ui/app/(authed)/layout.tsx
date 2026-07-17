import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { apiGet } from "@/lib/api";
import { BreadcrumbTitleProvider } from "./_components/BreadcrumbTitle";
import { Breadcrumbs } from "./_components/Breadcrumbs";
import { MobileNav } from "./_components/MobileNav";
import { Sidebar } from "./_components/Sidebar";
import { ThemeToggle } from "./_components/ThemeToggle";
import { Toaster } from "@/components/ui/toast";
import { ConfirmDialogProvider } from "@/components/ui/confirm-dialog";

interface AuthedLayoutProps {
  children: ReactNode;
}

const FALLBACK_STATION_NAME = "GenWave";

/** Wire shape of a `GET /api/stations` list item (see Host's StationDto). */
interface StationDto {
  id: number;
  name: string;
}

/**
 * Reads the live station name for the shell wordmark (SPEC F44.7, closes gitea-#195).
 * Falls back to the "GenWave" product brand on any failure — non-200, a
 * network error, or an empty station list — so the shell chrome never
 * renders blank or throws. `GET /api/stations` reads the live-effective name
 * on every call (post-V7), so a `Station:Name` settings edit shows up on the
 * shell's very next navigation with no client polling.
 */
async function fetchStationName(cookieHeader: string): Promise<string> {
  try {
    const response = await apiGet("/api/stations", { cookies: cookieHeader });
    if (!response.ok) {
      return FALLBACK_STATION_NAME;
    }
    const stations = (await response.json()) as StationDto[];
    return stations[0]?.name ?? FALLBACK_STATION_NAME;
  } catch {
    return FALLBACK_STATION_NAME;
  }
}

// Persistent shell for every authenticated route (SPEC F28.5). Auth itself is
// already enforced by middleware.ts on these paths — this layout only adds
// the chrome (sidebar, breadcrumb slot, theme toggle) around whatever the
// route renders; it does not re-check the session. Feedback primitives
// (SPEC F28.9/F28.14) mount here once: the toast viewport lives at the shell
// level, and ConfirmDialogProvider wraps the routed content so any page can
// call useConfirm() without re-mounting the dialog per page.
// BreadcrumbTitleProvider wraps both the header (Breadcrumbs) and the routed
// content — a nested page several levels below the header still needs to
// reach up and set the trailing crumb (STORY-090 AC4).
//
// Responsive shell (SPEC F28.13, STORY-093): Sidebar renders persistently
// but is `hidden` below 1024px (Tailwind's `lg:` breakpoint); MobileNav's
// hamburger — visible only below 1024px — opens the same nav as a
// focus-trapped Radix Dialog drawer instead. `min-w-0` on both the content
// column and <main> keeps a wide, unwrapped descendant from ever forcing the
// page body itself to scroll sideways — individual wide tables opt into
// their own `overflow-x-auto` container instead (AC2).
export default async function AuthedLayout({ children }: AuthedLayoutProps): Promise<ReactNode> {
  const cookieStore = await cookies();
  const stationName = await fetchStationName(cookieStore.toString());

  return (
    <BreadcrumbTitleProvider>
      <div className="flex min-h-screen bg-bg text-ink">
        <Sidebar stationName={stationName} />
        <div className="flex min-w-0 flex-1 flex-col">
          <header className="flex h-14 shrink-0 items-center justify-between gap-3 border-b border-line bg-surface px-4 sm:px-6">
            <div className="flex min-w-0 items-center gap-3">
              <MobileNav stationName={stationName} />
              <Breadcrumbs />
            </div>
            <ThemeToggle />
          </header>
          <main className="min-w-0 flex-1 p-4 sm:p-6">
            <ConfirmDialogProvider>{children}</ConfirmDialogProvider>
          </main>
        </div>
        <Toaster />
      </div>
    </BreadcrumbTitleProvider>
  );
}
