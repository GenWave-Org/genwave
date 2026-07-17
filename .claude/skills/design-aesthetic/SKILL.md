---
name: design-aesthetic
description: GenWave's visual identity — "Wireless" (warm retro radio). Color tokens, Fraunces/Source Sans 3 typography, spacing, component conventions, motion. Auto-surfaced when generating any UI. Canonical per ARCHITECTURE.md "Admin UI redesign" (SPEC F28).
---

# GenWave Design Aesthetic — "Wireless"

Locked 2026-07-11 from a three-direction mockup bake-off (gitea-#174). The identity
is **warm retro radio**: tube-amp cream, burnt orange, brass, a serif with
"70s record sleeve" energy — but disciplined like an ops console underneath.

## North-star references

- A 1970s hi-fi receiver faceplate — cream enamel, brass dial markings, one
  warm indicator lamp. The dashboard's now-playing card IS the faceplate.
- 70s record-sleeve typography (the reason Fraunces won) — display serif with
  real italics, used sparingly and large.
- The *discipline* (not the look) of Linear-style consoles: dense tables,
  quiet labels, tabular numbers — the retro warmth never costs operability.

## Aesthetic in one sentence

A 70s wireless set that happens to run a modern radio station — cream and
burnt orange, serif display over invisible sans, warm in both themes.

## Color tokens

Components consume **semantic tokens only** — never raw hex, never Tailwind
palette classes. A theme is a token set stamped via `data-theme` on `<html>`
(SPEC F28.1); light/dark ship first, more themes later are one token file each.

```css
/* light — "cream enamel" (default) */
--bg:        #F6EFE3;   /* page ground */
--surface:   #FDF8EE;   /* cards, panels, table bodies */
--surface-2: #EFE5D2;   /* sidebar, inset wells */
--line:      #DDD0B8;   /* borders, dividers */
--ink:       #2B2320;   /* body text */
--mute:      #77685C;   /* secondary text */
--accent:    #B94F29;   /* rust — primary action, active nav, ON AIR */
--accent-ink: #FDF8EE;  /* text/icons on --accent: cream reaches AA on the rust */
--accent-2:  #6F632F;   /* brass — labels, table headers, quiet emphasis */
                        /* darkened from #8A7B3F (Q11 review): the original
                           failed AA (4.5:1) as small text on all three light
                           grounds — this value clears bg/surface/surface-2 */
--danger:    #A63325;   /* deeper rust-red, distinct from accent */
--danger-ink: #FDF8EE;  /* text/icons on --danger */
--success:   #5C7A3F;   /* olive green */

/* dark — "walnut & brass" */
--bg:        #1E1713;
--surface:   #2A211B;
--surface-2: #241C16;
--line:      #3F342A;
--ink:       #F0E7D8;
--mute:      #A89A88;
--accent:    #D96A3D;   /* rust lifted for AA on dark */
--accent-ink: #1E1713;  /* dark theme inverts on-accent ink to deep walnut — cream
                            on the lifted dark rust only reaches ~2.8:1, walnut ≈4.99:1 */
--accent-2:  #B3A25E;   /* brass lifted */
--danger:    #E06A55;
--danger-ink: #2A211B;  /* text/icons on --danger */
--success:   #8FAE6A;
```

Usage: `--accent` is for the one thing that matters on the screen (primary
button, active nav item, on-air state). `--accent-2` (brass) carries all the
quiet structure — uppercase labels, table headers, dial markings — so rust
stays rare. Cards sit on `--surface` over `--bg`; the sidebar is `--surface-2`.
Semantic state colors (`--danger`, `--success`) are not accents — don't
decorate with them. On-accent and on-danger text/icons always use
`--accent-ink`/`--danger-ink` — never `--surface`/`--ink`, whose meaning
flips per theme and can silently fail contrast. Dark is a tuned token set,
not an inversion; verify AA contrast in both.

## Typography

- **Display:** Fraunces — wordmark, page titles, track titles. 400/600 +
  true italic. Track titles and the wordmark render *italic*; page headings
  upright.
- **Body/UI:** Source Sans 3 — everything operational: labels, tables, forms,
  buttons. 400/600 only.
- **Numbers:** Source Sans 3 with `font-variant-numeric: tabular-nums` on any
  column of digits (gain, times, counts).
- **Loading:** vendored woff2 in-repo via `next/font/local` — never
  `next/font/google`, never a font CDN (SPEC F28.3).
- **Scale:** base 16px; UI text .82–.88rem; page title 1.35rem; now-playing
  title 1.6rem. Uppercase micro-labels (.68–.7rem) always get
  `letter-spacing: .12em`–`.18em` and usually `--accent-2`.
- Fraunces is display-only. If body text ends up serif, it's wrong.

## Spacing & layout

- **Base unit:** 4px; component padding in .85–1.3rem range.
- **Shell:** 215px persistent sidebar (`--surface-2`, 2px `--line` right
  border) + main content. Collapses to a drawer under 1024px (F28.13).
- **Radius:** 6px on cards/panels, 3px on chips, 999px on badges. Nothing
  larger — big radii read SaaS, not receiver faceplate.
- **Borders over shadows:** 1px `--line` borders carry structure. The one
  permitted shadow trick is the hero card's double-ring
  (`box-shadow: 0 0 0 4px var(--bg), 0 0 0 5px var(--line)`) — faceplate trim,
  hero card only.
- **Density:** comfortable — an ops console, not a marketing page.

## Motion

- **Durations:** 120ms interactions, 200ms drawers/dialogs. Nothing slower.
- **Easing:** ease-out.
- **Signature:** the on-air dot pulses (1.6s opacity loop) — the indicator
  lamp. It is the only ambient animation on any page.
- **Never animates:** tables, text, layout shifts on poll updates (now-playing
  swaps in place, no slide/fade choreography).
- All nonessential motion gated behind `prefers-reduced-motion` (F28.14).

## Component conventions

- **Buttons:** primary = solid `--accent`, `--accent-ink` text; secondary = 1px
  `--line` border on `--surface`; destructive = solid `--danger`,
  `--danger-ink` text, only inside confirm dialogs. 6px radius, 40px min
  touch target.
- **Badges/chips:** pill (999px) for states (ON AIR, live/restart applyMode);
  3px-radius bordered chip for source tags (music/tts). TTS chips take
  `--accent` border+text.
- **Cards:** `--surface`, 1px `--line`, 6px radius. Hero (now-playing) card
  gets the double-ring trim and the dial-marking progress bar (frequency
  numbers under the bar in `--accent-2`).
- **Tables:** brass uppercase headers with 2px `--line` underline; hairline
  row dividers; right-aligned tabular numerals; rows selectable (F28.11
  contextual toolbar).
- **Forms:** labels above fields; validation inline at the field; mutation
  outcomes as toasts (F28.9). Confirm dialogs state the consequence in plain
  words ("This deletes the library. 14 tracks keep playing from…").
- **Empty states:** name the reason, offer the CTA (F28.10). Tone: calm radio
  operator, not cutesy ("Nothing in the safe library yet — generate the first
  announcement.").

## Anti-patterns (never produce these)

- Raw hex or Tailwind palette classes (`bg-orange-600`, `gray-100`) in
  components — tokens only.
- Cool grays anywhere. Every neutral is warm (biased toward the cream/brown
  axis).
- Fraunces in body/UI text, or faux-italic/faux-bold synthesis
  (`font-synthesis: none`).
- Big radii (`rounded-xl`+), drop shadows for depth, glassmorphism.
- Emoji as icons — icons are 1.5px-stroke geometric SVGs, `currentColor`.
- Rust (`--accent`) spent on decoration — if two rust elements compete on one
  screen, demote one to brass.
- `window.confirm()`, unstyled loading flashes, spinner-only pages.

## When to stop and ask

- Introducing a new semantic token or a new color meaning.
- A new page/section with no precedent in the shell IA.
- Any deviation from the palette or type pairing "just this once."
- Choosing an icon set or illustration style beyond the geometric strokes.
