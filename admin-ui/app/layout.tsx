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
        {/*
          Composed active-theme sheet (STORY-264, SPEC F102.3, PLAN T161/T162). Reached
          through next.config.ts's `/api/:path*` rewrite, so it is same-origin — no CORS,
          `style-src 'self'` unchanged. Root layout wraps `/login` too (no login/layout.tsx),
          which is exactly why AdminThemeEndpoints serves this anonymously — see that type's
          own remarks.

          `precedence` (React 19's stylesheet-Resource prop, not JSX position) is what fixes
          load order here, not where this tag sits in the tree: Next.js injects globals.css
          as its own precedence-managed stylesheet resource before this component's JSX is
          ever walked, and React appends a NEWLY-seen precedence value as a LATER group in
          <head> — so giving this link its own precedence value places it after globals.css
          regardless of where in the tree it is rendered. Reversing PLAN T162's hard
          precondition here means dropping `precedence` (making this a plain, unordered
          <link> — real order then stops being guaranteed) rather than moving the tag: a
          static HTML file's "linked after" has no direct analogue when one of the two
          sheets is a framework-managed bundle rather than another literal <link>.
        */}
        <link rel="stylesheet" href="/api/theme.css" precedence="theme" />
        {children}
        {/* gh-#7: version/edition stamp on every page — root layout wraps authed + login alike */}
        <VersionFooter />
      </body>
    </html>
  );
}
