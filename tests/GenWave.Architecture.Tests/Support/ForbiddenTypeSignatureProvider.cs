using System.Reflection.Metadata;

namespace GenWave.Architecture.Tests.Support;

/// <summary>
/// <see cref="HttpClientMetadataScan"/>'s leaf-matcher: decodes a field/parameter/return/local
/// signature blob (<see cref="System.Reflection.Metadata.Ecma335.SignatureDecoder{TType,TGenericContext}"/>'s
/// required shape) into the matched forbidden type's simple name, or <c>null</c> if the signature
/// mentions none of <paramref name="forbiddenNamesByHandle"/> anywhere in its structure.
/// Array/pointer/by-ref/pinned/generic wrappers all propagate their element's match upward (an
/// <c>HttpClient[]</c> field or a <c>Task&lt;HttpClient&gt;</c> return type still counts as
/// "depends on HttpClient"); primitives and generic type/method parameters never match on their
/// own. Naming the specific match here, in the same decode pass, avoids a second guess-which-one
/// pass once a hit is already known.
/// </summary>
internal readonly struct ForbiddenTypeSignatureProvider(IReadOnlyDictionary<EntityHandle, string> forbiddenNamesByHandle)
    : ISignatureTypeProvider<string?, object?>
{
    public string? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;

    public string? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        forbiddenNamesByHandle.GetValueOrDefault(handle);

    public string? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        forbiddenNamesByHandle.GetValueOrDefault(handle);

    public string? GetTypeFromSpecification(
        MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string? GetSZArrayType(string? elementType) => elementType;

    public string? GetArrayType(string? elementType, ArrayShape shape) => elementType;

    public string? GetByReferenceType(string? elementType) => elementType;

    public string? GetPointerType(string? elementType) => elementType;

    public string? GetPinnedType(string? elementType) => elementType;

    public string? GetGenericInstantiation(string? genericType, System.Collections.Immutable.ImmutableArray<string?> typeArguments) =>
        genericType ?? typeArguments.FirstOrDefault(argument => argument is not null);

    public string? GetGenericMethodParameter(object? genericContext, int index) => null;

    public string? GetGenericTypeParameter(object? genericContext, int index) => null;

    public string? GetModifiedType(string? modifier, string? unmodifiedType, bool isRequired) =>
        modifier ?? unmodifiedType;

    public string? GetFunctionPointerType(MethodSignature<string?> signature) =>
        signature.ReturnType ?? signature.ParameterTypes.FirstOrDefault(parameter => parameter is not null);
}
