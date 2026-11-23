namespace GenWave.Architecture.Tests.TalkRules.Construction;

/// <summary>
/// Resolves what the production registration (<c>AddTalkRules</c> plus logging) supplies, and a <see cref="NullObjectProxy"/>
/// for any other interface. This is the one construction seam: a rule that ships with a new collaborator is built with no
/// edit to the catalog. Concrete classes with no registration still fail loudly. Judge-only rules (R2) cannot take
/// collaborators at all, so only shaping and recording rules ever meet a stand-in.
/// </summary>
public sealed class CollaboratorFallbackProvider(IServiceProvider inner) : IServiceProvider
{
    public object? GetService(Type serviceType) =>
        inner.GetService(serviceType) ?? (serviceType.IsInterface && !serviceType.ContainsGenericParameters ? NullObjectProxy.For(serviceType) : null);
}
