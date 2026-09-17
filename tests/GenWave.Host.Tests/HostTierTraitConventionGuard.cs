namespace GenWave.Host.Tests;

/// <summary>
/// Host-side twin of <c>GenWave.MediaLibrary.Tests.IntegrationTraitConventionGuard</c> (SPEC F177.3,
/// STORY-439, PLAN T504): asserts <see cref="Support.TierTraitConventionGuard"/> reports zero
/// violations over this assembly's exported types, using its default
/// <see cref="Support.TierTraitConventionGuard.StackFixtureTypes"/> list. Pure reflection, no
/// container, no database — it runs in tier 1 and fails fast at the source instead of as an opaque
/// container error when a class silently tries to bring up Kokoro or the live compose stack from
/// inside <c>dotnet test --filter "Category!=Integration"</c>.
/// </summary>
/// <remarks>
/// Named <c>HostTierTraitConventionGuard</c> rather than the bare <c>TierTraitConventionGuard</c> the
/// <see cref="Support.TierTraitConventionGuard"/> reflection helper carries: this type lives directly
/// in the <c>GenWave.Host.Tests</c> namespace, which encloses <c>GenWave.Host.Tests.Specs</c> — a
/// same-named type here would out-rank Story439_TestTiers.cs's own
/// <c>using GenWave.Host.Tests.Support;</c> for every unqualified <c>TierTraitConventionGuard</c>
/// call in that file (C# resolves enclosing-namespace declarations before sibling-level usings).
/// </remarks>
public sealed class HostTierTraitConventionGuard
{
    [Fact]
    public void EveryFixtureConsumingTestClassIsCategorisedByTier()
    {
        var offenders = Support.TierTraitConventionGuard.FindViolations(typeof(KokoroFixture).Assembly.GetExportedTypes());

        Assert.True(
            offenders.Count == 0,
            "Test classes consuming KokoroFixture or a stack fixture (SPEC F177.3) without "
                + "[Trait(\"Category\", \"Integration\")] at class level or on every fact — CI's tier "
                + "1 filter would try to start a container it cannot reach:\n  "
                + string.Join("\n  ", offenders));
    }
}
