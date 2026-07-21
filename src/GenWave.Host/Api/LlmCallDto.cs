namespace GenWave.Host.Api;

/// <summary>
/// One row of <c>GET /api/llm-calls</c> (SPEC F73.1-F73.2, STORY-196): a single completed LLM call
/// exactly as <see cref="GenWave.Tts.LlmCallRing"/> captured it. <see cref="PromptSystem"/>/
/// <see cref="PromptUser"/>/<see cref="Response"/> carry the FULL text — this is admin-only debug
/// detail, never a public surface, and never persisted (see <see cref="GenWave.Tts.LlmCallRing"/>'s
/// own remarks). <see cref="PromptChars"/>/<see cref="ResponseChars"/> are a cheap at-a-glance size
/// for the table view; the full text is what the expandable row shows.
/// </summary>
public sealed record LlmCallDto(
    long Seq,
    DateTimeOffset StartedAt,
    long ElapsedMs,
    string Status,
    string? StatusDetail,
    string Mode,
    string? PromptSystem,
    string? PromptUser,
    string? Response,
    int PromptChars,
    int ResponseChars);
