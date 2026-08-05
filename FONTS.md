# Curated fonts — process & provenance

Scope: the **GenWave-vendored curated font set** the theme system (SPEC F102/F103) composes into
`@font-face` rules at runtime and serves from the canonical `GET /fonts/{file}` route
(`src/GenWave.Host/Api/FontEndpoints.cs`). This document is the **process** PLAN T188 exists to
establish — it closes ARCHITECTURE.md's two "Theme system" TODOs ("Font licensing per face",
"Page-weight ceiling") **for the curated set**. The face list itself is a separate decision each time the set grows
(PLAN T189 added JetBrains Mono + Grenze Gotisch, Dean-approved 2026-08-05); this document fixes
the steps every face — today's or a later one — must clear before it ships, plus the mechanism
that enforces it. Known recipe limitation: the step-3 `pyftsubset` invocation drops OFL name
records (IDs 13/14) from the output; copyright (ID 0) survives and the licence is recorded in
the provenance file, but a future recipe revision should add `--name-IDs='*'` and re-subset.

**Owner-uploaded fonts are explicitly out of scope** (SPEC F103.10, ARCHITECTURE "Theme system" §
Community Catalog v2 → "Curated-font *process* now, faces at `/plan`; no owner uploads"). This
process only ever admits a face **GenWave itself vendors** into the repo; an owner's own font
upload is a later slice with its own licensing-attestation and moderation surface
(ARCHITECTURE's "Owner uploads" TODO).

## The process — four steps, in order

Every new curated face goes through all four before a theme manifest is allowed to reference it.

### 1. OFL-confirm

Before vendoring anything, confirm the upstream family carries a permissive licence — the SIL Open
Font License (OFL) or an equivalent (e.g. Apache 2.0) — and locate its **canonical upstream
repository** (not just wherever it happened to be downloaded from). Record the licence's SPDX
identifier (`OFL-1.1`, …) and the upstream repo URL; both become required fields in the provenance
record (step 2). A family without a clearly OFL-or-equivalent-licensed upstream is not vendored,
full stop — this is a legal confirm, not a "looks free" guess.

### 2. Record provenance

Add one entry to the machine-readable provenance record —
`src/GenWave.Host/wwwroot/fonts/fonts-provenance.json` — **before** the face ships:

```jsonc
{
  "family": "Fraunces",                                            // matches the theme manifest's fonts.*.family
  "file": "fraunces-variable-latin.woff2",                         // bare filename under /fonts/
  "sourceUrl": "https://github.com/undercasetype/Fraunces",        // canonical upstream repo (step 1)
  "license": "OFL-1.1",                                            // SPDX id (step 1)
  "version": null,                                                 // upstream tag/commit, when determinable
  "subset": "latin",                                                // the subsetting step below
  "bytes": 67304                                                    // measured after subsetting (step 4)
}
```

This record is the **source of truth** `ThemeFontProvenanceValidator` (step 3 of the runtime
enforcement below) checks every theme manifest's font asset `src` against — a face that is not in
this file cannot be referenced by any theme, shipped or imported (see "Enforcement" below).

### 3. Latin-subset

Vendored faces are **latin-only** — this app has no non-latin copy to render, and a full,
all-scripts variable font is many times the size of just what's used. Subset with
[fonttools](https://github.com/fonttools/fonttools)' `pyftsubset` (the same tool Google Fonts'
own build pipeline uses), against the codepoint range Google Fonts itself defines for its `latin`
subset (verified 2026-08-05 against `fonts.googleapis.com`'s own served CSS):

```bash
pip install fonttools brotli
pyftsubset SourceFont.ttf \
  --output-file=family-name-variable-latin.woff2 \
  --flavor=woff2 \
  --layout-features='*' \
  --unicodes="U+0000-00FF,U+0131,U+0152-0153,U+02BB-02BC,U+02C6,U+02DA,U+02DC,U+0304,U+0308,U+0329,U+2000-206F,U+20AC,U+2122,U+2191,U+2193,U+2212,U+2215,U+FEFF,U+FFFD"
```

Output filename convention: `{family-kebab-case}[-italic]-variable-latin.woff2` — matches the
three faces already vendored and `FontEndpoints`' literal per-file switch.

### 4. Measure against the ceiling

Measure the subsetted file's byte size (`stat -c%s file.woff2` or equivalent) and record it in the
`bytes` field (step 2). A theme's **summed, distinct** referenced-face bytes (all vendored fonts
its `display`/`sans` roles name, once each) must clear the per-theme ceiling below —
`ThemeFontProvenanceValidator` enforces this at load/import time, so an over-budget theme is
rejected rather than silently shipped.

## Per-theme byte ceiling

**Measurement.** GenWave ships five vendored faces today: the base pair — Fraunces variable,
Fraunces italic variable, Source Sans 3 variable (both embedded themes reference all three) —
plus PLAN T189's two Dean-approved additions, JetBrains Mono variable (a monospace role) and
Grenze Gotisch variable (a display alternate). All five are `latin`-subsetted:

| File | Bytes |
|---|---|
| `fraunces-variable-latin.woff2` | 67,304 |
| `fraunces-italic-variable-latin.woff2` | 42,228 |
| `source-sans-3-variable-latin.woff2` | 28,740 |
| `jetbrains-mono-variable-latin.woff2` | 47,392 |
| `grenze-gotisch-variable-latin.woff2` | 51,992 |
| **Total** | **237,656 (≈232.1 KiB)** |

**Chosen ceiling: 200 KiB (204,800 bytes) per theme.** Rationale: ~50% headroom over the base
pair's measured ~135 KiB — enough for one additional face on top of the full base pair without a
rubber-stamp "just raise the ceiling" outcome, while still keeping a THEME SWITCH's worst-case
added page weight (ARCHITECTURE "Theme system": only the *active* theme's faces are ever emitted)
in the same order of magnitude as a single hero image rather than "the heaviest thing on the page"
the deferred TODO warned about. The constant lives in code as
`ThemeFontProvenanceValidator.PerThemeByteCeilingBytes` — update both this document and that
constant together if the number ever changes.

**Pairing constraint.** The base pair (138,272 B) plus JetBrains Mono alone (185,664 B) fits; the
base pair plus Grenze Gotisch alone (190,264 B) fits; the base pair plus **both** new faces
(237,656 B) exceeds the 204,800-byte ceiling. A theme may therefore add at most **one** of the two
PLAN T189 faces on top of the full base pair — e.g. JetBrains Mono + Grenze Gotisch + Source Sans 3
alone (no Fraunces) totals 128,124 B and also fits.

## Enforcement — the validator

`ThemeFontProvenanceValidator` (`src/GenWave.Host/Theming/ThemeFontProvenanceValidator.cs`) checks
two things against `fonts-provenance.json` (`FontProvenanceCatalog`):

1. **Existence** — every font asset `src` a theme's manifest declares resolves to a face in the
   provenance record. `ThemeManifestParser.FontSrcPattern` already pins the URL *shape*
   (`/fonts/<name>.woff2`, no traversal, no off-origin URL); this is the missing *existence* check —
   a manifest naming `/fonts/nonexistent.woff2` is now rejected at load/import time, naming the
   missing face and the whole vendored set, instead of only failing once a browser requests it and
   `FontEndpoints`' closed switch 404s it per-visitor.
2. **Per-theme byte ceiling** — the theme's distinct referenced faces' summed bytes (from the
   provenance record) must clear the ceiling above.

Wired at exactly the two places a theme manifest **enters the running system** — see
`ThemeFontProvenanceValidator`'s own remarks for the full placement reasoning:

- `ThemeCatalog.LoadShipped()` — every shipped manifest (also covers `ThemeCatalog.CreateForStation`'s
  initial state, which reuses `LoadShipped()`'s already-validated result).
- `ThemesImportController.Import` — the only `station.theme` write path (SPEC F103.6); a rejection
  here is a `400` naming the offending face(s), never a partial or silent store.

Deliberately **not** re-checked on every `ThemeCatalog.ReloadOwnerThemesAsync` reload (owner rows
are guaranteed to already satisfy this the moment they were imported, since the import route above
is the only writer). `ThemePreviewController` **does** call the same validator (commit `d43305d`,
Dean-approved 2026-08-05: "preview refuses what import refuses") — an operator must never be sold a
live preview of a theme the import route would go on to reject. This is an additive, non-persisting
check of the same rule against ephemeral preview input; the import gate above remains the one place
that guarantees every *persisted* row satisfies SPEC F103.10.
