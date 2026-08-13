using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L7/L8's shared detector: <see cref="HttpClientMetadataScan"/>'s exact SRM idiom (raw metadata
/// tables, matching <see cref="AssemblyReferenceScan"/>'s house convention — see that class's remarks
/// for why ArchUnitNET is blind to a call inside an async lambda's doubly-nested compiler-generated
/// state machine), sized down from a whole forbidden TYPE family to a single forbidden METHOD
/// signature, and sharing its IL-walking/attribution mechanics via <see cref="IlTokenWalker"/> (T277
/// review: the two scans had each grown an identical copy of both before this extraction).
///
/// <b>Why a method-level match needs more than <see cref="HttpClientMetadataScan"/>'s type-level
/// one.</b> That scan forbids depending on a TYPE at all — any field, parameter, local, return, or IL
/// token naming it counts, and one hit per type is enough to report. This scan forbids CALLING one
/// specific overload of a NAMED type's method while a sibling overload sharing that name stays
/// perfectly legal (<see cref="GenWave.Core.Abstractions.ITtsSynthesizer.SynthesizeAsync(string, string, System.Threading.CancellationToken)"/>
/// vs. its context-aware sibling) — so the match has to reach into the <c>MemberReference</c>'s own
/// name and, where <see cref="ForbiddenMemberSignature.ParameterCount"/> says so, its parameter count,
/// not merely the type it hangs off. And unlike that scan (which stops at the type's first hit — every
/// forbidden type in the family means the same thing for that law), this one keeps walking a type's
/// whole method set and reports every DISTINCT forbidden member it finds, because L8's exemption
/// granularity is per-(type, member), not per-type — collapsing two different forbidden-member hits on
/// the same type into one report would risk the second, non-exempt one going unseen.
///
/// <b>Boundary — where this scan cannot see a real bypass.</b> Named explicitly, not comfort-claimed
/// away (T277 review: an earlier draft's "unreachable in practice" language for item 4 below was
/// proven false and deleted).
/// <list type="number">
/// <item><description><b>Concrete-type dispatch.</b> A caller holding a CONCRETE implementer type
/// instead of the type a <see cref="ForbiddenMemberSignature"/> names (e.g. a Host class typed to
/// <c>KokoroTtsSynthesizer</c> instead of <c>ITtsSynthesizer</c>, calling
/// <c>.SynthesizeAsync(text, voice, ct)</c> on it directly) targets THAT concrete type's own member —
/// a different (namespace, name) pair than the forbidden signature declares — and is not caught
/// unless a forbidden-signature entry names every concrete implementer too. This is REACHABLE code
/// today (Host already references Tts), not a theoretical gap.</description></item>
/// <item><description><b>Reflection.</b> <c>type.GetMethod("SynthesizeAsync", ...).Invoke(...)</c>
/// never emits the token-bearing <c>InlineMethod</c>/<c>InlineField</c>/<c>InlineTok</c>/<c>InlineType</c>
/// instruction <see cref="IlTokenWalker"/> matches against — a call built this way is
/// invisible.</description></item>
/// <item><description><b><c>calli</c>.</b> Inherited from <see cref="IlTokenWalker"/>, shared with L3's
/// <see cref="HttpClientMetadataScan"/>: a function-pointer call's own signature is never
/// decoded.</description></item>
/// <item><description><b>Same-assembly, same-type self-calls, by ANY implementer — not only a
/// forbidden interface's own default-implementation body.</b> Matching only cross-assembly
/// <c>TypeReference</c>/<c>MemberReference</c> rows means a type calling one of its OWN members via a
/// same-assembly <c>MethodDef</c> token is invisible, regardless of why that self-call exists. See
/// <see cref="TtsSynthesizeContextSeam"/>'s own remarks for the concrete, PROVEN harm this creates for
/// L7 specifically — this is the gap that matters most, and the one the deleted "unreachable in
/// practice" sentence wrongly papered over.</description></item>
/// </list>
/// </summary>
internal static class MemberCallSiteScan
{
    /// <summary>Evaluates "no type in <paramref name="assemblyPaths"/> outside
    /// <paramref name="isExempt"/> calls any of <paramref name="forbiddenMembers"/>" — the exemption
    /// check is a PARAMETER (mirroring <see cref="HttpClientSeams.FindViolations"/>'s own shape one
    /// law over), not baked into this function, so the SAME detector backs both a law's real
    /// production fact (the real <see cref="MemberCallSiteExemption"/> list) and a fixture
    /// self-proof (a synthetic forbidden signature + exemption list over a fixture namespace) without
    /// either one re-implementing the scan.</summary>
    public static IReadOnlyList<LawViolation> FindViolations(
        IEnumerable<string> assemblyPaths,
        IReadOnlyList<ForbiddenMemberSignature> forbiddenMembers,
        string lawId,
        Func<string, string, bool> isExempt)
    {
        var violations = new List<LawViolation>();

        foreach (var assemblyPath in assemblyPaths)
        {
            foreach (var (typeFullName, description) in FindCallingTypes(assemblyPath, forbiddenMembers))
            {
                if (isExempt(typeFullName, description))
                    continue;

                violations.Add(new LawViolation(lawId, typeFullName, $"references {description} directly"));
            }
        }

        return violations;
    }

    /// <summary>Every outermost type in <paramref name="assemblyPath"/> that CALLS (or otherwise
    /// references via an IL token — a delegate construction over the member counts too, the same way
    /// <see cref="HttpClientMetadataScan"/> treats any token-bearing instruction as a reference) any of
    /// <paramref name="forbiddenMembers"/>, one entry per (type, forbidden member) pair actually
    /// found — never collapsed to one entry per type, unlike the type-level scan this mirrors (see the
    /// class remarks for why).</summary>
    private static IReadOnlyList<(string TypeFullName, string ForbiddenDescription)> FindCallingTypes(
        string assemblyPath, IReadOnlyList<ForbiddenMemberSignature> forbiddenMembers)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var forbiddenDescriptionsByHandle = ResolveForbiddenDescriptionsByHandle(reader, forbiddenMembers);
        if (forbiddenDescriptionsByHandle.Count == 0)
            return [];

        var results = new HashSet<(string TypeFullName, string ForbiddenDescription)>();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            foreach (var description in FindForbiddenCallsIn(reader, peReader, typeHandle, forbiddenDescriptionsByHandle))
                results.Add((IlTokenWalker.OutermostDeclaringTypeFullName(reader, typeHandle), description));
        }

        return results.ToList();
    }

    /// <summary>Every <c>MemberReference</c> matching one of <paramref name="forbiddenMembers"/> by
    /// (declaring type, member name, and — when the signature says so — parameter count), keyed by its
    /// own handle so <see cref="IlTokenWalker.ResolveTokens{T}"/> can recognize the exact IL token a
    /// call site embeds.</summary>
    private static Dictionary<EntityHandle, string> ResolveForbiddenDescriptionsByHandle(
        MetadataReader reader, IReadOnlyList<ForbiddenMemberSignature> forbiddenMembers)
    {
        var declaringTypeMatches = new Dictionary<EntityHandle, List<ForbiddenMemberSignature>>();
        foreach (var handle in reader.TypeReferences)
        {
            var typeReference = reader.GetTypeReference(handle);
            var ns = reader.GetString(typeReference.Namespace);
            var name = reader.GetString(typeReference.Name);

            var matches = forbiddenMembers
                .Where(member => member.DeclaringNamespace == ns && member.DeclaringName == name)
                .ToList();
            if (matches.Count > 0)
                declaringTypeMatches[handle] = matches;
        }

        var names = new Dictionary<EntityHandle, string>();
        if (declaringTypeMatches.Count == 0)
            return names;

        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (!declaringTypeMatches.TryGetValue(member.Parent, out var candidates))
                continue;

            var memberName = reader.GetString(member.Name);
            var match = candidates.FirstOrDefault(candidate =>
                candidate.MemberName == memberName
                && (candidate.ParameterCount is not { } expected || ParameterCountMatches(reader, member, expected)));

            if (match is not null)
                names[handle] = match.Description;
        }

        return names;
    }

    /// <summary>Reads a <c>MemberRefSig</c>'s own parameter count straight off its compressed-integer
    /// header (ECMA-335 II.23.2.2/.3) — no full <c>SignatureDecoder</c> pass needed when all that is
    /// ever asked is "how many parameters", not what type each one is. A non-method signature (a field
    /// reference sharing the forbidden name by coincidence — none do today, but nothing here assumes
    /// it) never matches: its <see cref="SignatureHeader.Kind"/> reads as <see cref="SignatureKind.Field"/>,
    /// not <see cref="SignatureKind.Method"/>.</summary>
    private static bool ParameterCountMatches(MetadataReader reader, MemberReference member, int expectedCount)
    {
        var blob = reader.GetBlobReader(member.Signature);
        var header = blob.ReadSignatureHeader();
        if (header.Kind != SignatureKind.Method)
            return false;

        if (header.IsGeneric)
            blob.ReadCompressedInteger(); // generic parameter count — none of today's forbidden methods are generic.

        return blob.ReadCompressedInteger() == expectedCount;
    }

    /// <summary>Every distinct forbidden member <paramref name="typeHandle"/>'s own methods call,
    /// across its WHOLE method set (never stopping at the first hit — see the class remarks).</summary>
    private static IEnumerable<string> FindForbiddenCallsIn(
        MetadataReader reader,
        PEReader peReader,
        TypeDefinitionHandle typeHandle,
        IReadOnlyDictionary<EntityHandle, string> forbiddenDescriptionsByHandle)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0)
                continue; // abstract, extern (P/Invoke), or otherwise body-less — nothing to scan.

            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            foreach (var hit in IlTokenWalker.ResolveTokens(body.GetILReader(), forbiddenDescriptionsByHandle))
                found.Add(hit);
        }

        return found;
    }
}
