using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// L3's detector (STORY-291 AC1). Reads a compiled assembly's own metadata tables directly
/// (<c>System.Reflection.Metadata</c>, matching <see cref="AssemblyReferenceScan"/>'s house idiom —
/// not Mono.Cecil, which rides in only transitively via ArchUnitNET's own dependency closure and
/// would need its own <c>PackageReference</c> to use directly) instead of ArchUnitNET's object graph.
///
/// <b>Why this exists instead of the ArchUnitNET-based detector every other law uses.</b> ArchUnitNET
/// builds its type graph from <c>TypeDefinition</c> rows it walks top-down from each namespace; a
/// compiler-generated type nested INSIDE another compiler-generated type — an async lambda's state
/// machine (<c>&lt;...&gt;d__N</c>), itself nested inside the closure class
/// (<c>&lt;&gt;c__DisplayClass_N</c>) capturing the lambda — never surfaces in that graph. Proven
/// experimentally: a stray <c>new HttpClient()</c> inside an async lambda passed to a minimal-API
/// handler (<c>MediaEndpoints.cs</c>'s <c>/media/random</c> handler) left the ArchUnitNET-based scan
/// green. The compiled metadata's own <c>TypeDef</c> table has no such gap — it is flat by
/// construction (nesting is a separate <c>NestedClass</c> mapping, not a tree the reader has to
/// recurse), so every compiler-generated type at any nesting depth is just another row to visit.
/// Verified: scanning this way finds the exact async-lambda probe ArchUnitNET missed, and reproduces
/// the real production graph's known-good count with zero false positives (see
/// <see cref="HttpClientSeams"/>'s remarks for the exact numbers).
///
/// <b>Attribution.</b> A compiler-generated type is meaningless to name in a violation message — a
/// reviewer needs to know which hand-written type is responsible. Every hit is rolled up to its
/// OUTERMOST declaring type (walking the <c>NestedClass</c> chain to the top) before being reported,
/// so an async lambda's state machine, however many closure/state-machine layers deep, is attributed
/// to the ordinary class or top-level-statement <c>Program</c> type a reviewer actually authored.
///
/// <b>What "references" means here.</b> A type counts as depending on a forbidden type if any of its
/// members mention it anywhere a signature or an IL instruction can: a field's type, a method's
/// parameter or return type, a method body's local variable, or any token an IL instruction embeds
/// directly (construction, a method call on the type, a cast, a box/unbox, <c>ldtoken</c>, ...).
/// Between them these cover every shape the designated seams and their violators actually take:
/// typed-client constructor injection (parameter type), a raw <c>new HttpClient()</c> (IL token,
/// and/or a hoisted state-machine field if the value's lifetime crosses an <c>await</c>), and
/// <c>IHttpClientFactory.CreateClient(...)</c>'s return value assigned to a local (local-variable
/// type). One known, narrow gap: a <c>calli</c> function-pointer call's own signature is not decoded
/// (its <c>InlineSig</c> operand is skipped, not resolved) — <c>delegate*</c> function pointers do
/// not appear anywhere in GenWave's HTTP call sites today, and C# never emits <c>calli</c> for an
/// ordinary method call. Two more, both verified narrow: (a) a forbidden type reached ONLY through a
/// <c>MethodSpec</c>/<c>TypeSpec</c> token whose result is never itself typed or member-called — e.g.
/// <c>sp.GetRequiredService&lt;HttpClient&gt;()</c> with the result immediately discarded — isn't
/// matched by this scan; the moment the client is actually named (a field/parameter/local/return
/// type) or used (a member call), that use site reds, so no working outbound path escapes
/// undetected. (b) The scan matches against the <c>TypeReference</c> table, so it is blind to a
/// forbidden type's own USE inside the assembly that DEFINES that type — irrelevant here, since
/// GenWave defines none of <see cref="HttpClientSeams.ForbiddenTypes"/>; they are all BCL types.
/// </summary>
internal static class HttpClientMetadataScan
{
    /// <summary>Every outermost type in <paramref name="assemblyPath"/> that depends on any of
    /// <paramref name="forbiddenTypes"/>, one entry per type (whichever forbidden type its scan finds
    /// first — a violator depending on two forbidden types at once is still one violation of this
    /// type). A <paramref name="forbiddenTypes"/> entry this assembly never references at all costs
    /// nothing beyond the initial <c>TypeReference</c> table scan — no <c>TypeReference</c> row to
    /// match means nothing further to look for.</summary>
    public static IReadOnlyList<(string TypeFullName, string ForbiddenTypeName)> FindReferencingTypes(
        string assemblyPath, IReadOnlyList<(string Namespace, string Name)> forbiddenTypes)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var forbiddenNamesByHandle = ResolveForbiddenNamesByHandle(reader, forbiddenTypes);
        if (forbiddenNamesByHandle.Count == 0)
            return [];

        var provider = new ForbiddenTypeSignatureProvider(forbiddenNamesByHandle);
        var decoder = new SignatureDecoder<string?, object?>(provider, reader, null);

        var results = new List<(string TypeFullName, string ForbiddenTypeName)>();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var forbiddenName = FindForbiddenNameUsedBy(reader, peReader, typeHandle, decoder, forbiddenNamesByHandle);
            if (forbiddenName is not null)
                results.Add((OutermostDeclaringTypeFullName(reader, typeHandle), forbiddenName));
        }

        // One violation per outermost type even when several of its compiler-generated nested types
        // (or several members of the same type) each independently reference a forbidden type.
        return results.GroupBy(r => r.TypeFullName).Select(g => g.First()).ToList();
    }

    /// <summary>Every <c>TypeReference</c> matching one of <paramref name="forbiddenTypes"/> by
    /// (namespace, name), plus every <c>MemberReference</c> whose declaring type is one of those —
    /// a constructor or method CALLED on a forbidden type (e.g. <c>HttpClient</c>'s <c>.ctor</c>, or
    /// <c>HttpMessageInvoker.SendAsync</c>) carries the same meaning as a direct type reference for
    /// this law's purposes, and IL instructions address a called member by its <c>MemberRef</c>
    /// token, not the type's own token.</summary>
    private static Dictionary<EntityHandle, string> ResolveForbiddenNamesByHandle(
        MetadataReader reader, IReadOnlyList<(string Namespace, string Name)> forbiddenTypes)
    {
        var names = new Dictionary<EntityHandle, string>();

        foreach (var handle in reader.TypeReferences)
        {
            var typeReference = reader.GetTypeReference(handle);
            var ns = reader.GetString(typeReference.Namespace);
            var name = reader.GetString(typeReference.Name);

            foreach (var forbidden in forbiddenTypes)
            {
                if (forbidden.Namespace == ns && forbidden.Name == name)
                {
                    names[handle] = forbidden.Name;
                    break;
                }
            }
        }

        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (names.TryGetValue(member.Parent, out var parentName))
                names[handle] = parentName;
        }

        return names;
    }

    private static string? FindForbiddenNameUsedBy(
        MetadataReader reader,
        PEReader peReader,
        TypeDefinitionHandle typeHandle,
        SignatureDecoder<string?, object?> decoder,
        IReadOnlyDictionary<EntityHandle, string> forbiddenNamesByHandle)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            var blob = reader.GetBlobReader(field.Signature);
            var hit = decoder.DecodeFieldSignature(ref blob);
            if (hit is not null)
                return hit;
        }

        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);

            var signatureBlob = reader.GetBlobReader(method.Signature);
            var signature = decoder.DecodeMethodSignature(ref signatureBlob);
            var signatureHit = signature.ReturnType ?? signature.ParameterTypes.FirstOrDefault(p => p is not null);
            if (signatureHit is not null)
                return signatureHit;

            if (method.RelativeVirtualAddress == 0)
                continue; // abstract, extern (P/Invoke), or otherwise body-less — nothing to scan.

            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);

            if (!body.LocalSignature.IsNil)
            {
                var localsSignature = reader.GetStandaloneSignature(body.LocalSignature);
                var localsBlob = reader.GetBlobReader(localsSignature.Signature);
                var locals = decoder.DecodeLocalSignature(ref localsBlob);
                var localsHit = locals.FirstOrDefault(l => l is not null);
                if (localsHit is not null)
                    return localsHit;
            }

            var ilHit = ScanIlTokens(body.GetILReader(), forbiddenNamesByHandle);
            if (ilHit is not null)
                return ilHit;
        }

        return null;
    }

    /// <summary>Walks a method body's raw IL, decoding just enough of each instruction (via the
    /// operand-size table <see cref="IlOperandTable"/> builds from .NET's own <see cref="OpCodes"/> —
    /// no hand-copied opcode list to fall out of sync) to reach the 4-byte metadata token every
    /// type/field/method/token operand carries, and checks it against the forbidden set.</summary>
    private static string? ScanIlTokens(BlobReader il, IReadOnlyDictionary<EntityHandle, string> forbiddenNamesByHandle)
    {
        while (il.RemainingBytes > 0)
        {
            var opByte = il.ReadByte();
            var opCode = opByte == 0xFE ? (short)(0xFE00 | il.ReadByte()) : opByte;

            if (!IlOperandTable.OperandTypeByOpCode.TryGetValue(opCode, out var operandType))
            {
                throw new InvalidOperationException(
                    $"Unrecognized IL opcode 0x{opCode:X4} — the operand table (built from " +
                    "System.Reflection.Emit.OpCodes) is out of date for this runtime.");
            }

            switch (operandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                    il.ReadByte();
                    break;
                case OperandType.InlineVar:
                    il.ReadInt16();
                    break;
                case OperandType.InlineBrTarget or OperandType.InlineI or OperandType.ShortInlineR
                    or OperandType.InlineString or OperandType.InlineSig:
                    il.ReadInt32();
                    break;
                case OperandType.InlineI8 or OperandType.InlineR:
                    il.ReadInt64();
                    break;
                case OperandType.InlineSwitch:
                    var targetCount = il.ReadInt32();
                    for (var i = 0; i < targetCount; i++)
                        il.ReadInt32();
                    break;
                case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType:
                    var token = il.ReadInt32();
                    var entity = MetadataTokens.EntityHandle(token);
                    if (forbiddenNamesByHandle.TryGetValue(entity, out var name))
                        return name;
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled IL operand type {operandType}.");
            }
        }

        return null;
    }

    private static string OutermostDeclaringTypeFullName(MetadataReader reader, TypeDefinitionHandle typeHandle)
    {
        var current = typeHandle;
        var declaringType = reader.GetTypeDefinition(current).GetDeclaringType();
        while (!declaringType.IsNil)
        {
            current = declaringType;
            declaringType = reader.GetTypeDefinition(current).GetDeclaringType();
        }

        var outer = reader.GetTypeDefinition(current);
        var ns = reader.GetString(outer.Namespace);
        var name = reader.GetString(outer.Name);

        // Top-level-statement Program.cs compiles to a bare "Program" type in the GLOBAL namespace
        // (C# forbids wrapping top-level statements in a namespace block) — reader.GetString on an
        // empty NamespaceDefinitionHandle yields "", which a naive "ns + "." + name" would render as
        // the wrong ".Program".
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }
}
