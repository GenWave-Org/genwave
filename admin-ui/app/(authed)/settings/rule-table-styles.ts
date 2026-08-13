/**
 * Shared cell classNames for the settings page's row-editor tables — `CorrectionsSettingControl`'s
 * `Tts:Corrections` table and `PronunciationRulesControl`'s `/api/pronunciations` table both
 * render the same "brass header, bordered input cell" shape (PLAN T145 review should-fix: this
 * was a byte-identical copy in each file). `EngineByKindSettingControl`'s own `HEADER_CELL` stays
 * a local copy — its input cells are `<select>`s with a different className, so only the header
 * treatment would overlap, not enough to earn a shared home there too.
 */
export const CELL_INPUT_CLASSES =
  "h-9 w-full rounded-[6px] border border-line bg-surface px-2 text-[0.85rem] text-ink disabled:opacity-50";
export const HEADER_CELL =
  "py-2 pr-3 pl-3 text-left text-[0.68rem] font-semibold uppercase tracking-[0.12em] text-accent-2";
