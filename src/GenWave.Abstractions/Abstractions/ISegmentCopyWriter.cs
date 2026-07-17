using GenWave.Core.Domain;

namespace GenWave.Core.Abstractions;

/// <summary>
/// Expands a <see cref="SegmentRequest"/> into the spoken copy for a segment (SPEC F34.1). The
/// terminal implementation (<c>TemplateCopyWriter</c>) is pure and MUST always succeed; an LLM-backed
/// implementation MAY layer in front of it, but any failure there falls back to the template — this
/// port never throws toward <see cref="ITtsSegmentSource"/>.
/// </summary>
public interface ISegmentCopyWriter
{
    /// <summary>Returns the text to synthesize for <paramref name="request"/>, and its cache provenance.</summary>
    Task<SegmentCopy> WriteAsync(SegmentRequest request, CancellationToken ct);
}
