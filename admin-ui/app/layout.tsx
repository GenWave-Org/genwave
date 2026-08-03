import type { Metadata } from "next";
import type { ReactNode } from "react";
import { cookies } from "next/headers";
import { parseTheme, THEME_COOKIE_NAME } from "@/lib/theme";
import { VersionFooter } from "@/components/VersionFooter";
import "./globals.css";

// Fraunces (display serif) and Source Sans 3 (operational sans) are declared as plain @font-face
// rules in globals.css, served same-origin from GET /fonts/{file} (PLAN T173, SPEC F102) via
// next.config.ts's rewrite — not next/font/local. A theme manifest (T156's FontSrcPattern) needs
// a stable, nameable URL for a vendored face; next/font/local content-hashes into
// .next/static/media/* at build time, which no manifest authored ahead of a build could ever
// name. Accepted cost (ratified, ARCHITECTURE.md "Theme system"): admin loses Next's font
// preloading — plain @font-face + font-display: swap in its place.

export const metadata: Metadata = {
  title: "GenWave Admin",
  description: "GenWave radio station administration",
};

interface RootLayoutProps {
  children: ReactNode;
}

// Reads the genwave-theme cookie during the server render and stamps data-theme
// on <html> before any HTML reaches the browser — first paint never flashes the
// wrong theme (SPEC F28.4). A garbage or absent cookie value parses to null, so
// no attribute is rendered at all and globals.css's prefers-color-scheme media
// query decides the default, matching what the client would have picked anyway.
export default async function RootLayout({
  children,
}: RootLayoutProps): Promise<ReactNode> {
  const cookieStore = await cookies();
  const theme = parseTheme(cookieStore.get(THEME_COOKIE_NAME)?.value);

  return (
    <html lang="en" data-theme={theme ?? undefined}>
      <body>
        {children}
        {/* gh-#7: version/edition stamp on every page — root layout wraps authed + login alike */}
        <VersionFooter />
      </body>
    </html>
  );
}
