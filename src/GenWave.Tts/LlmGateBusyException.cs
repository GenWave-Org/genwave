namespace GenWave.Tts;

/// <summary>
/// Control-flow signal inside <see cref="LlmCopyWriter"/>: a bounded single-flight wait (preview
/// path only — on-air callers wait unboundedly) missed its <c>Llm:PreviewQueueWaitSeconds</c>
/// budget because the gate is held by another render. Thrown before the gate is ever acquired and
/// before anything is attempted (no <see cref="LlmCallRing"/> entry, no WARN — nothing failed),
/// and never escapes the writer: <see cref="LlmCopyWriter.WritePreviewAsync"/> maps it to
/// <see cref="GenWave.Core.Domain.PersonaPreviewResult.Busy"/>.
/// </summary>
internal sealed class LlmGateBusyException : Exception;
