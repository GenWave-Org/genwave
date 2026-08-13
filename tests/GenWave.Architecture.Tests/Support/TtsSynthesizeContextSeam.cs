namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L7's named, dated constant (SPEC F126.4, STORY-323, PLAN T277): no production type outside the two
/// named relay implementations invokes <see cref="GenWave.Core.Abstractions.ITtsSynthesizer"/>'s
/// context-less <c>SynthesizeAsync(string, string, CancellationToken)</c> overload directly —
/// bypassing <c>TtsRenderContext</c> and the kind/rules/pace it carries to the engine (ARCHITECTURE.md
/// "Architecture governance"). The audit finding this law closes: two former bypasses
/// (<c>TtsPreviewController</c>, PLAN T274; <c>SafeSegmentAuthor</c>, PLAN T276) both called the plain
/// overload directly and both are fixed to build a context and call the richer overload instead — this
/// law makes a third one unreachable rather than merely fixed by review.
///
/// <b>Detector.</b> <see cref="MemberCallSiteScan"/> — L3's <see cref="HttpClientMetadataScan"/> SRM
/// idiom, sized to one method signature; see that class's own remarks for the full boundary
/// enumeration (concrete-type dispatch, reflection, <c>calli</c>, same-assembly self-calls) this law
/// inherits.
///
/// <b>The same-assembly self-call gap, and the real harm it creates for THIS law specifically.</b>
/// <see cref="GenWave.Core.Abstractions.ITtsSynthesizer"/>'s context-aware overload carries a default
/// body that itself forwards into the plain overload — the "discard-by-DIM" shape, silently dropping
/// <see cref="GenWave.Core.Domain.TtsRenderContext.Kind"/> (and every other context-only field) for
/// any implementer that never overrides it. That forwarding call is a same-assembly, same-type call
/// (<see cref="MemberCallSiteScan"/>'s boundary item 4), invisible to this scan by construction — true
/// for the interface's OWN default body, and true for ANY implementer whose own context overload
/// forwards into its own plain overload the same way. That is CORRECT for the two named relays below,
/// which forward properly (through their own richer overload, never losing context). It is NOT a proof
/// that every implementer does: a future <c>ITtsSynthesizer</c> implementer added to
/// <c>GenWave.Tts</c> whose context overload self-forwards into its own plain overload — reproducing
/// the exact discard-by-DIM harm this law exists to catch — is completely invisible to this law,
/// regardless of whether it is named below. Verified at T277 review: a throwaway type shaped exactly
/// this way stayed green. This law proves only that no OUTSIDE caller reaches the plain overload
/// directly; it cannot prove every implementer's own internal forwarding is honest — that remains a
/// code-review-time property, same as before this law existed.
///
/// <b>The two named exemptions.</b> <see cref="GenWave.Tts.NormalizingTtsSynthesizer"/> and
/// <see cref="GenWave.Tts.FallbackTtsSynthesizer"/> each DEFINE (never call) the plain overload as a
/// one-line wrap/relay into their OWN context overload — <c>SynthesizeAsync(new TtsRenderContext(text,
/// voice, Kind: null), ct)</c> — the shape every <c>ITtsSynthesizer</c> implementer must provide since
/// the plain overload is the interface's one REQUIRED member. Naming them here is a forward guard, not
/// a correction of a live violation: verified at T277 adoption, neither class's own method body calls
/// the plain overload on anything — each relay call resolves directly to its own richer overload
/// (`this.SynthesizeAsync(TtsRenderContext, ...)`, never through the interface), so this law's
/// production fact finds zero violations before the exemption list is even consulted.
/// </summary>
internal static class TtsSynthesizeContextSeam
{
    /// <summary>The one forbidden signature: <see cref="GenWave.Core.Abstractions.ITtsSynthesizer"/>'s
    /// plain, context-less overload — 3 parameters, disambiguating it from the 2-parameter
    /// context-aware sibling that shares its name.</summary>
    public static readonly ForbiddenMemberSignature ForbiddenSignature = new(
        DeclaringNamespace: "GenWave.Core.Abstractions",
        DeclaringName: "ITtsSynthesizer",
        MemberName: "SynthesizeAsync",
        ParameterCount: 3,
        Description: "ITtsSynthesizer.SynthesizeAsync(string, string, CancellationToken)");

    /// <summary>The two relay types (F126.4's "outside the two relays"), each a TYPE-level exemption
    /// (<see cref="MemberCallSiteExemption.ForbiddenMember"/> null — this law has only one forbidden signature,
    /// so type- and member-level exemption mean the same thing here) — see the class remarks for why
    /// neither actually needs the exemption today, and why it is named anyway.</summary>
    public static readonly IReadOnlyList<MemberCallSiteExemption> DesignatedRelays = new[]
    {
        new MemberCallSiteExemption("GenWave.Tts.NormalizingTtsSynthesizer"),
        new MemberCallSiteExemption("GenWave.Tts.FallbackTtsSynthesizer"),
    };

    /// <summary>Evaluates "no type in <paramref name="assemblyPaths"/> outside
    /// <see cref="DesignatedRelays"/> calls <see cref="ForbiddenSignature"/>", via the shared
    /// <see cref="MemberCallSiteScan.FindViolations"/>.</summary>
    public static IReadOnlyList<LawViolation> FindViolations(IEnumerable<string> assemblyPaths) =>
        MemberCallSiteScan.FindViolations(
            assemblyPaths,
            [ForbiddenSignature],
            LawId.L7,
            (type, member) => DesignatedRelays.Any(exemption => exemption.Matches(type, member)));
}
