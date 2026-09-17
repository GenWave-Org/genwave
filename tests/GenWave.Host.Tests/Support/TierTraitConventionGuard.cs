using System.Reflection;

namespace GenWave.Host.Tests.Support;

/// <summary>
/// The Host tier guard (SPEC F177.3, STORY-439, PLAN T504) — the reflection twin of
/// <c>GenWave.MediaLibrary.Tests.IntegrationTraitConventionGuard</c>: any test class that takes a
/// <c>KokoroFixture</c> (constructor, <c>IClassFixture&lt;&gt;</c> or <c>ICollectionFixture&lt;&gt;</c>,
/// or a <c>[Collection(name)]</c> whose <c>[CollectionDefinition(name)]</c> class implements
/// <c>ICollectionFixture&lt;&gt;</c> for it) or any type in <see cref="StackFixtureTypes"/>, and lacks
/// <c>[Trait("Category", "Integration")]</c> at class level or on every fact, is a violation — it
/// would try to start a container or a live compose stack in the PR lane. Only facts declared on
/// the class itself count (<c>DeclaredOnly</c>): facts inherited from a base test class are not
/// checked, and no Host test class inherits facts today.
/// </summary>
/// <remarks>
/// Today the only stack fixture is Story230's <c>RequestsArtworkFlywheelFixture</c> — its flywheel
/// facts need the LIVE compose stack (requests enabled, PublicBaseUrl set, a real catalog), so
/// F177.1 puts them in tier 2. F178's stream-capture gate runs as a shell script, not a fixture, so
/// it never appears here; there are no other stack/capture fixtures in Host.Tests today.
/// </remarks>
internal static class TierTraitConventionGuard
{
    /// <summary>Fixture types that need a full stack or an audio capture — tier 2 by definition.</summary>
    public static IReadOnlyList<Type> StackFixtureTypes { get; } =
        [typeof(GenWave.Host.Tests.Specs.RequestsArtworkFlywheelFixture)];

    /// <summary>Full names of the offending classes (one entry per class, never per fact).</summary>
    public static IReadOnlyList<string> FindViolations(
        IEnumerable<Type> types, IReadOnlyCollection<Type>? stackFixtureTypes = null)
    {
        var watchedTypes = new HashSet<Type>(stackFixtureTypes ?? StackFixtureTypes) { typeof(KokoroFixture) };
        var typeList = types as IReadOnlyList<Type> ?? types.ToList();

        var collectionDefinitionsByAssembly = typeList
            .Select(type => type.Assembly)
            .Distinct()
            .ToDictionary(assembly => assembly, CollectionDefinitionsByName);

        var offenders = new List<string>();

        foreach (var type in typeList)
        {
            if (!ConsumesWatchedFixture(type, watchedTypes, collectionDefinitionsByAssembly[type.Assembly]))
                continue;
            if (HasIntegrationTrait(type.GetCustomAttributesData()))
                continue;

            var testMethods = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(IsTestMethod)
                .ToList();
            if (testMethods.Count == 0)
                continue; // e.g. a bare [CollectionDefinition] marker class — nothing to guard.

            var untraited = testMethods
                .Where(method => !HasIntegrationTrait(method.GetCustomAttributesData()))
                .Select(method => method.Name)
                .ToList();
            if (untraited.Count == 0)
                continue; // every fact already carries the trait.

            offenders.Add($"{type.FullName} (missing [Trait(\"Category\", \"Integration\")] on: {string.Join(", ", untraited)})");
        }

        return offenders;
    }

    static bool ConsumesWatchedFixture(Type type, HashSet<Type> watchedTypes, IReadOnlyDictionary<string, Type> collectionDefinitionsByName)
    {
        if (ImplementsFixtureInterfaceFor(type, watchedTypes))
            return true;

        if (TryGetCollectionName(type, out var collectionName)
            && collectionDefinitionsByName.TryGetValue(collectionName, out var definitionType)
            && ImplementsFixtureInterfaceFor(definitionType, watchedTypes))
            return true;

        return type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Any(ctor => ctor.GetParameters().Any(parameter => watchedTypes.Contains(parameter.ParameterType)));
    }

    static bool ImplementsFixtureInterfaceFor(Type type, HashSet<Type> watchedTypes) =>
        type.GetInterfaces().Any(implemented =>
            implemented.IsGenericType
            && (implemented.GetGenericTypeDefinition() == typeof(IClassFixture<>)
                || implemented.GetGenericTypeDefinition() == typeof(ICollectionFixture<>))
            && watchedTypes.Contains(implemented.GetGenericArguments()[0]));

    static bool TryGetCollectionName(Type type, out string name)
    {
        var attribute = type.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType == typeof(CollectionAttribute));
        if (attribute is { ConstructorArguments.Count: 1 } && attribute.ConstructorArguments[0].Value is string collectionName)
        {
            name = collectionName;
            return true;
        }

        name = "";
        return false;
    }

    /// <summary>Maps every <c>[CollectionDefinition(name)]</c> class in <paramref name="assembly"/>
    /// to its name, so a <c>[Collection(name)]</c> consumer can be traced back to the fixture(s) its
    /// collection actually shares.</summary>
    static IReadOnlyDictionary<string, Type> CollectionDefinitionsByName(Assembly assembly)
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in assembly.GetTypes())
        {
            if (TryGetCollectionDefinitionName(type, out var name))
                map[name] = type;
        }

        return map;
    }

    static bool TryGetCollectionDefinitionName(Type type, out string name)
    {
        var attribute = type.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType == typeof(CollectionDefinitionAttribute));
        if (attribute is { ConstructorArguments.Count: 1 } && attribute.ConstructorArguments[0].Value is string definitionName)
        {
            name = definitionName;
            return true;
        }

        name = "";
        return false;
    }

    static bool HasIntegrationTrait(IEnumerable<CustomAttributeData> attributes) =>
        attributes.Any(a =>
            a.AttributeType == typeof(TraitAttribute)
            && a.ConstructorArguments.Count == 2
            && a.ConstructorArguments[0].Value as string == "Category"
            && a.ConstructorArguments[1].Value as string == "Integration");

    static bool IsTestMethod(MethodInfo method) =>
        method.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(FactAttribute) || a.AttributeType.IsSubclassOf(typeof(FactAttribute)));
}
