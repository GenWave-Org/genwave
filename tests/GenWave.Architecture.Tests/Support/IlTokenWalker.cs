using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// Shared IL-body primitives L3's <see cref="HttpClientMetadataScan"/> and L7/L8's
/// <see cref="MemberCallSiteScan"/> both consume — extracted at T277 review once both scans needed
/// the identical mechanics, each having grown its own copy independently. One
/// <see cref="IlOperandTable"/>-driven token walk, one outermost-declaring-type rollup: a future
/// runtime opcode addition, or a fix to the <c>Program.cs</c> global-namespace edge case, is now a
/// one-place fix, not two hand-copied copies quietly drifting apart.
/// </summary>
internal static class IlTokenWalker
{
    /// <summary>Walks <paramref name="il"/> instruction by instruction (via
    /// <see cref="IlOperandTable"/>, built from the runtime's own <c>OpCodes</c>), yielding whatever
    /// <paramref name="tokensOfInterest"/> resolves for every token-bearing instruction's 4-byte
    /// metadata token — construction, a method call, a field access, a cast/box/unbox,
    /// <c>ldtoken</c>, a delegate construction over a method (<c>ldftn</c>/<c>ldvirtftn</c>) — in
    /// encounter order, duplicates included. A caller that only needs the first hit takes
    /// <c>FirstOrDefault()</c> (<see cref="HttpClientMetadataScan"/>'s shape — one forbidden type
    /// found is as good as a second, for that law); a caller that needs every DISTINCT hit collects
    /// into its own set (<see cref="MemberCallSiteScan"/>'s shape — L8's per-member exemption
    /// granularity depends on not stopping at the first).
    ///
    /// <b>The one known gap, inherited by every caller.</b> A <c>calli</c> function-pointer call's
    /// own signature is not decoded (its <c>InlineSig</c> operand is read and discarded, not
    /// resolved) — no GenWave call site emits <c>calli</c> for an ordinary method call; C# never
    /// does.</summary>
    public static IEnumerable<T> ResolveTokens<T>(BlobReader il, IReadOnlyDictionary<EntityHandle, T> tokensOfInterest)
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
                    if (tokensOfInterest.TryGetValue(entity, out var value))
                        yield return value;
                    break;
                default:
                    throw new InvalidOperationException($"Unhandled IL operand type {operandType}.");
            }
        }
    }

    /// <summary>Rolls a (possibly compiler-generated, possibly doubly-nested) <c>TypeDefinition</c>
    /// up to the ordinary, hand-written type a reviewer actually authored — an async lambda's state
    /// machine nested inside its closure class attributes to the enclosing method's declaring type,
    /// however many layers deep. Top-level-statement <c>Program.cs</c> compiles to a bare
    /// <c>Program</c> type in the GLOBAL namespace (C# forbids wrapping top-level statements in a
    /// namespace block) — <c>reader.GetString</c> on an empty <c>NamespaceDefinitionHandle</c> yields
    /// <c>""</c>, which a naive <c>ns + "." + name</c> would render as the wrong
    /// <c>".Program"</c>.</summary>
    public static string OutermostDeclaringTypeFullName(MetadataReader reader, TypeDefinitionHandle typeHandle)
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

        return ns.Length == 0 ? name : $"{ns}.{name}";
    }
}
