import type { ReactNode } from "react";

// Wireless icon convention (.claude/skills/design-aesthetic/SKILL.md): inline
// 1.5px-stroke geometric SVGs on a 16x16 viewBox, currentColor — no icon font,
// no filled glyphs, no emoji.

export interface IconProps {
  className?: string;
}

function IconBase({
  className,
  children,
}: IconProps & { children: ReactNode }): ReactNode {
  return (
    <svg
      viewBox="0 0 16 16"
      width="16"
      height="16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={className}
    >
      {children}
    </svg>
  );
}

export function DashboardIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <rect x="2" y="2" width="5" height="5" rx="1" />
      <rect x="9" y="2" width="5" height="5" rx="1" />
      <rect x="2" y="9" width="5" height="5" rx="1" />
      <rect x="9" y="9" width="5" height="5" rx="1" />
    </IconBase>
  );
}

export function LiveIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <circle cx="8" cy="8" r="1.3" fill="currentColor" stroke="none" />
      <path d="M5.5 5.5a4 4 0 0 0 0 5" />
      <path d="M10.5 5.5a4 4 0 0 1 0 5" />
    </IconBase>
  );
}

export function CatalogIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <line x1="2" y1="4" x2="14" y2="4" />
      <line x1="2" y1="8" x2="14" y2="8" />
      <line x1="2" y1="12" x2="10" y2="12" />
    </IconBase>
  );
}

export function SafeContentIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <path d="M8 1.5l5 2v4c0 3.5-2.2 6-5 7-2.8-1-5-3.5-5-7v-4l5-2z" />
      <path d="M5.7 8l1.6 1.6 3-3.2" />
    </IconBase>
  );
}

/** DJ persona nav glyph (SPEC F35.7) — a microphone: capsule head, pickup arc, stand. */
export function PersonaIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <rect x="6" y="1.6" width="4" height="7" rx="2" />
      <path d="M4 7.5a4 4 0 0 0 8 0" />
      <line x1="8" y1="11.5" x2="8" y2="14" />
      <line x1="5.5" y1="14" x2="10.5" y2="14" />
    </IconBase>
  );
}

/** Booth log nav glyph (PLAN T40, STORY-195) — a ruled ledger page: bound frame + header rule +
 * text lines, reading as "the log book" distinct from Catalog's plain unbound rule lines. */
export function BoothLogIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <rect x="3" y="1.75" width="10" height="12.5" rx="1.2" />
      <line x1="3" y1="4.5" x2="13" y2="4.5" />
      <line x1="5.5" y1="7.25" x2="10.5" y2="7.25" />
      <line x1="5.5" y1="9.75" x2="10.5" y2="9.75" />
      <line x1="5.5" y1="12.25" x2="9" y2="12.25" />
    </IconBase>
  );
}

export function SettingsIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <circle cx="8" cy="8" r="2.2" />
      <path d="M8 1.6v1.6M8 12.8v1.6M14.4 8h-1.6M3.2 8H1.6M12.1 3.9l-1.1 1.1M5 10l-1.1 1.1M12.1 12.1l-1.1-1.1M5 6l-1.1-1.1" />
    </IconBase>
  );
}

export function SignOutIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <path d="M6 2H3a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h3" />
      <path d="M10.5 5.5L14 8l-3.5 2.5" />
      <path d="M14 8H6" />
    </IconBase>
  );
}

export function SunIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <circle cx="8" cy="8" r="2.6" />
      <path d="M8 1.5v1.6M8 12.9v1.6M2.6 8H1M15 8h-1.6M3.8 3.8l1.1 1.1M11.1 11.1l1.1 1.1M12.2 3.8l-1.1 1.1M4.9 11.1l-1.1 1.1" />
    </IconBase>
  );
}

export function MoonIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <path d="M13.5 9.6A5.7 5.7 0 1 1 6.4 2.5a4.6 4.6 0 0 0 7.1 7.1z" />
    </IconBase>
  );
}

/** Hamburger trigger for the sub-1024px nav drawer (SPEC F28.13). */
export function MenuIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <line x1="2" y1="4.5" x2="14" y2="4.5" />
      <line x1="2" y1="8" x2="14" y2="8" />
      <line x1="2" y1="11.5" x2="14" y2="11.5" />
    </IconBase>
  );
}

/** Closes the nav drawer (SPEC F28.13); also the "never play" glyph on {@link RatingControls}
 * (F33.11) — an X is an X in both contexts. */
export function CloseIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <line x1="3.5" y1="3.5" x2="12.5" y2="12.5" />
      <line x1="12.5" y1="3.5" x2="3.5" y2="12.5" />
    </IconBase>
  );
}

/** Vote-up glyph (SPEC F33.11, STORY-114) — {@link RatingControls}. */
export function VoteUpIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <path d="M8 12.5v-9" />
      <path d="M4.5 7L8 3.5L11.5 7" />
    </IconBase>
  );
}

/** Vote-down glyph (SPEC F33.11, STORY-114) — {@link RatingControls}. */
export function VoteDownIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <path d="M8 3.5v9" />
      <path d="M4.5 9L8 12.5L11.5 9" />
    </IconBase>
  );
}

/** Restore-to-rotation glyph — the never-play toggle's flagged state (SPEC F33.11, STORY-114),
 * a circular restore arrow so it reads distinctly from {@link CloseIcon}'s flat X. */
export function RestoreIcon(props: IconProps): ReactNode {
  return (
    <IconBase {...props}>
      <path d="M3.6 8a4.5 4.5 0 1 0 1.3-3.2" />
      <path d="M3.2 3.2v2.7h2.7" />
    </IconBase>
  );
}
