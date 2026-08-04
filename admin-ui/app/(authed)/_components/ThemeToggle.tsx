"use client";

import { useEffect, useState, type ReactNode } from "react";
import { Tooltip } from "@/components/ui/tooltip";
import { MODE_COOKIE_MAX_AGE_SECONDS, MODE_COOKIE_NAME, type Mode } from "@/lib/theme";
import { MoonIcon, SunIcon } from "./icons";

/** Reads the mode currently in effect: the explicit data-theme attribute if
 *  set, otherwise the resolved prefers-color-scheme default. */
function resolveCurrentMode(): Mode {
  const attr = document.documentElement.getAttribute("data-theme");
  if (attr === "light" || attr === "dark") {
    return attr;
  }
  return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
}

/**
 * Client-only mode toggle: cycles light/dark, applies data-theme to <html>
 * immediately (no reload) and persists the choice in the genwave-mode
 * cookie so the next server render picks it up (SPEC F28.4). Starts at
 * `null` and resolves the real value in an effect so the first client render
 * matches the server's markup — no hydration mismatch. Icon-only, so it
 * carries a hover/focus tooltip with the same copy as its aria-label
 * (SPEC F62.1–F62.2).
 */
export function ThemeToggle(): ReactNode {
  const [mode, setMode] = useState<Mode | null>(null);

  useEffect(() => {
    setMode(resolveCurrentMode());
  }, []);

  function handleToggle(): void {
    const next: Mode = mode === "dark" ? "light" : "dark";
    document.documentElement.setAttribute("data-theme", next);
    document.cookie = `${MODE_COOKIE_NAME}=${next}; path=/; max-age=${MODE_COOKIE_MAX_AGE_SECONDS}; SameSite=Lax`;
    setMode(next);
  }

  const isDark = mode === "dark";
  const label = isDark ? "Switch to light theme" : "Switch to dark theme";

  return (
    <Tooltip label={label}>
      <button
        type="button"
        onClick={handleToggle}
        aria-label={label}
        className="flex h-10 w-10 items-center justify-center rounded-[6px] text-mute transition-colors duration-[120ms] ease-out hover:bg-surface-2 hover:text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
      >
        {isDark ? <SunIcon /> : <MoonIcon />}
      </button>
    </Tooltip>
  );
}
