namespace GenWave.Host.Api;

/// <summary>
/// One row of <c>GET /api/llm-calls</c> (SPEC F73.1-F73.2, STORY-196): a single completed LLM call
/// exactly as <see cref="GenWave.Tts.LlmCallRing"/> captured it. <see cref="PromptSystem"/>/
/// <see cref="PromptUser"/>/<see cref="Response"/> carry the FULL text — this is admin-only debug
/// detail, never a public surface, and never persisted (see <see cref="GenWave.Tts.LlmCallRing"/>'s
/// own remarks). <see cref="PromptChars"/>/<see cref="ResponseChars"/> are a cheap at-a-glance size
/// for the table view; the full text is what the expandable row shows. <see cref="PersonaName"/>
/// (gh-#429) is who authored the call — <see langword="null"/> for a persona-less render, never an
/// empty string. <see cref="Kind"/> (SPEC F127.11, PLAN T282) is <c>"copy"</c> for every ordinary
/// segment-copy call or <c>"crosstalk"</c> for a <see cref="GenWave.Tts.CrosstalkScriptWriter"/> call
/// — so an operator can tell "why was there no banter" apart from an ordinary blurb miss.
/// <see cref="Cause"/>/<see cref="Model"/> (SPEC F139.1-F139.2, STORY-353, PLAN T334) carry
/// <see cref="GenWave.Tts.LlmCallRecord.Cause"/>/<see cref="GenWave.Tts.LlmCallRecord.Model"/>
/// verbatim, lowercased the same way <see cref="Status"/>/<see cref="Mode"/>/<see cref="Kind"/>
/// already are — the per-row half of the F139 taxonomy reaching this wire; <see cref="Model"/> is
/// never <see langword="null"/> for the same reason that field already isn't on the domain record.
/// </summary>
public sealed record LlmCallDto(
    long Seq,
    string? PersonaName,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    string Status,
    string? StatusDetail,
    string Mode,
    string? PromptSystem,
    string? PromptUser,
    string? Response,
    int PromptChars,
    int ResponseChars,
    string Kind,
    string Cause,
    string Model);
