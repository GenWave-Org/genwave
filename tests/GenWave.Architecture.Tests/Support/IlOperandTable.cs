using System.Reflection;
using System.Reflection.Emit;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// Every IL opcode's operand shape, read off <see cref="OpCodes"/>'s own public static fields (via
/// reflection, once) instead of a hand-maintained table — the BCL's own opcode list can never drift
/// out of sync with itself. <see cref="HttpClientMetadataScan"/>'s IL walker uses this to know how
/// many operand bytes to skip (or read as a metadata token) for each instruction it encounters.
/// </summary>
internal static class IlOperandTable
{
    public static readonly IReadOnlyDictionary<short, OperandType> OperandTypeByOpCode = Build();

    private static Dictionary<short, OperandType> Build()
    {
        var table = new Dictionary<short, OperandType>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opCode)
                table[opCode.Value] = opCode.OperandType;
        }

        return table;
    }
}
