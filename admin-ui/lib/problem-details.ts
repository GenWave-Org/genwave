/** ProblemDetails (RFC 7807) body shape returned by this station's api on a failed mutation or
 * fetch — `detail` plus the `type` URI (gh-#486) a caller uses to tell two different causes of the
 * same status code apart without parsing `detail`'s human text. `field`/`ruleId` (PLAN T404) are
 * the two extension members a save-time validator 400 rides (e.g. `AdsController.ScriptViolationProblem`)
 * — optional so every caller that never sees them (the vast majority) is unaffected. `referencedBy`
 * (T563, SPEC F204.3) replaces the retired `referenced-themes.ts` prose parser with the referrers'
 * names, structured. */
interface ProblemDetailsBody {
  detail?: string;
  type?: string;
  field?: string;
  ruleId?: string;
  referencedBy?: unknown;
}

function isProblemDetailsBody(raw: unknown): raw is ProblemDetailsBody {
  return typeof raw === "object" && raw !== null;
}

/** Narrows `referencedBy` to a non-empty string array, dropping any non-string entry rather than
 * trusting the wire blindly — `undefined` when the field is absent, not an array, or every entry
 * failed the check (the same "degrade, never throw" posture this file's other fields already hold
 * to). */
function narrowReferencedBy(raw: unknown): string[] | undefined {
  if (!Array.isArray(raw)) return undefined;
  const names = raw.filter((entry): entry is string => typeof entry === "string" && entry !== "");
  return names.length > 0 ? names : undefined;
}

/**
 * Reads a failed response's ProblemDetails `detail`, `type` (gh-#486), `field`/`ruleId` (PLAN T404),
 * and `referencedBy` (T563/T564) in ONE body read — a `Response` body can only be consumed once.
 * {@link readErrorMessage} is this reader's own detail-only shorthand.
 */
export async function readProblemDetails(
  resp: Response
): Promise<{ detail: string; type?: string; field?: string; ruleId?: string; referencedBy?: string[] }> {
  try {
    const raw = (await resp.json()) as unknown;
    if (isProblemDetailsBody(raw)) {
      return {
        detail: typeof raw.detail === "string" && raw.detail !== "" ? raw.detail : `Unexpected error (${resp.status})`,
        type: typeof raw.type === "string" ? raw.type : undefined,
        field: typeof raw.field === "string" ? raw.field : undefined,
        ruleId: typeof raw.ruleId === "string" ? raw.ruleId : undefined,
        referencedBy: narrowReferencedBy(raw.referencedBy),
      };
    }
  } catch {
    // malformed or empty body — fall through to the generic message
  }
  return { detail: `Unexpected error (${resp.status})` };
}

/**
 * Extracts the ProblemDetails `detail` message from a failed response, falling back to a generic
 * `"Unexpected error (status)"` message — the detail-first convention every CRUD mutation, the
 * persona import flow, and the Persona Catalog detail-panel fetch all share (contrast
 * `PersonaPreview`'s own title-first `readPreviewFailureMessage` and `VoicePackInstallModal`'s own
 * title-first `readInstallFailureTitle`, both of which have their own documented reason to differ).
 * Hoisted here (T102 review) so this one small, deterministic reader has a single house
 * implementation instead of a per-feature-folder copy.
 */
export async function readErrorMessage(resp: Response): Promise<string> {
  return (await readProblemDetails(resp)).detail;
}
