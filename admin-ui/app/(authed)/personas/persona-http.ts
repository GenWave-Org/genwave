/** ProblemDetails body shape returned on 400/409/413 from the personas CRUD and export/import
 * endpoints (SPEC F35.4, F79.6). */
interface ProblemDetailsBody {
  detail?: string;
}

function isProblemDetailsBody(raw: unknown): raw is ProblemDetailsBody {
  return typeof raw === "object" && raw !== null;
}

/**
 * Extracts the ProblemDetails `detail` message from a failed response, falling back to a generic
 * one — the detail-first convention every CRUD mutation and the import flow on this page share
 * (contrast `PersonaPreview`'s title-first reader, documented on that function). Shared here
 * rather than duplicated between `PersonasClient` and `PersonaImportPanel`; this also carries the
 * F79.6 newer-schema-major message ("names both versions") and the 413 oversized message verbatim.
 */
export async function readErrorMessage(resp: Response): Promise<string> {
  try {
    const raw = (await resp.json()) as unknown;
    if (isProblemDetailsBody(raw) && typeof raw.detail === "string" && raw.detail !== "") {
      return raw.detail;
    }
  } catch {
    // malformed or empty body — fall through to the generic message
  }
  return `Unexpected error (${resp.status})`;
}
