using System.Reflection;

namespace GenWave.MediaLibrary.Tests;

/// <summary>
/// House-convention guard. Any test class that joins the shared database collection
/// (<see cref="DatabaseCollection.Name"/>) MUST also be categorised
/// <c>[Trait("Category", "Integration")]</c> — class-level or on every fact — so CI's
/// <c>dotnet test --filter "Category!=Integration"</c> excludes it. A DB-backed class without
/// the trait runs inside the SDK-only CI job container, tries to bring up the fixture-managed
/// Postgres whose bind mounts don't resolve through the host daemon, and the container exits 126.
/// This guard is pure reflection (no database), so it runs in CI and fails fast at the source
/// instead of as an opaque container error. See .gitea/workflows/dotnet-ci.yml and DatabaseFixture.
/// (The parallel ffmpeg dependency in Story016 can't be detected by reflection — that one is on review.)
/// </summary>
public sealed class IntegrationTraitConventionGuard
{
    [Fact]
    public void EveryDatabaseBackedTestMethodIsCategorisedIntegration()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(DatabaseCollection).Assembly.GetTypes())
        {
            if (!JoinsDatabaseCollection(type)) continue;

            var classIsIntegration = HasIntegrationTrait(type.GetCustomAttributesData());

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!IsTestMethod(method)) continue;
                if (classIsIntegration || HasIntegrationTrait(method.GetCustomAttributesData())) continue;
                offenders.Add($"{type.FullName}.{method.Name}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "DB-backed test methods missing [Trait(\"Category\", \"Integration\")] — CI cannot start the "
                + "fixture Postgres, so these would fail with container exit 126:\n  "
                + string.Join("\n  ", offenders));
    }

    static bool JoinsDatabaseCollection(Type type) =>
        type.GetCustomAttributesData().Any(a =>
            a.AttributeType == typeof(CollectionAttribute)
            && a.ConstructorArguments.Count == 1
            && a.ConstructorArguments[0].Value as string == DatabaseCollection.Name);

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
