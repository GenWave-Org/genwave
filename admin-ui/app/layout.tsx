import type { Metadata } from "next";
import type { ReactNode } from "react";
import localFont from "next/font/local";
import { cookies } from "next/headers";
import { cn } from "@/lib/utils";
import { parseTheme, THEME_COOKIE_NAME } from "@/lib/theme";
import { VersionFooter } from "@/components/VersionFooter";
import "./globals.css";

// Fraunces — display serif (wordmark, page titles, track titles). Variable font
// (opsz 9..144, wght axis) vendored as woff2 latin subsets; loaded via
// next/font/local so no request ever leaves the build/runtime (SPEC F28.3).
const fraunces = localFont({
  src: [
    {
      path: "./fonts/Fraunces-Variable-latin.woff2",
      weight: "400 600",
      style: "normal",
    },
    {
      path: "./fonts/Fraunces-Italic-Variable-latin.woff2",
      weight: "400",
      style: "italic",
    },
  ],
  variable: "--font-display",
  display: "swap",
});

// Source Sans 3 — operational sans (labels, tables, forms, buttons, body).
// Variable font (wght axis), 400/600 only, no italic.
const sourceSans = localFont({
  src: [
    {
      path: "./fonts/SourceSans3-Variable-latin.woff2",
      weight: "400 600",
      style: "normal",
    },
  ],
  variable: "--font-sans",
  display: "swap",
});

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
    <html
      lang="en"
      data-theme={theme ?? undefined}
      className={cn(fraunces.variable, sourceSans.variable)}
    >
      <body>
        {children}
        {/* gh-#7: version/edition stamp on every page — root layout wraps authed + login alike */}
        <VersionFooter />
      </body>
    </html>
  );
}
