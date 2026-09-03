// STORY-384 — The contract grows without breaking anyone (F157.1, F158.1 · T390)
using System.Reflection;
using GenWave.Architecture.Tests.Support;
using GenWave.Core.Domain;

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureAbstractionsContractGrows
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePluginSpiExistsAndIsMinimal
    {
        [Fact]
        public void IGenWavePluginExposesExactlyNameAndRegister()
        {
            var type = ResolveAbstractionsType("GenWave.Core.Abstractions.IGenWavePlugin");

            Assert.Equal(new[] { "Name:String", "Register(IPluginHost):Void" }, PublicApiShape(type));
        }

        [Fact]
        public void IPluginHostExposesExactlyTheThreeAddAndSettingMembers()
        {
            var type = ResolveAbstractionsType("GenWave.Core.Abstractions.IPluginHost");

            Assert.Equal(
                new[]
                {
                    "AddAdSpotSource(IAdSpotSource):Void",
                    "AddContextProvider(IContextProvider):Void",
                    "Setting(String):String",
                },
                PublicApiShape(type));
        }
    }

    public sealed class ScenarioTheAdsSeamExists
    {
        [Fact]
        public void IAdSpotSourceExposesExactlyGetNextSpotAsync()
        {
            var type = ResolveAbstractionsType("GenWave.Core.Abstractions.IAdSpotSource");

            Assert.Equal(new[] { "GetNextSpotAsync(CancellationToken):ValueTask`1" }, PublicApiShape(type));
        }
    }

    public sealed class ScenarioTheEnumsAppendNeverReorder
    {
        [Fact]
        public void SegmentKindAdIsTheLastMemberAndPriorValuesHold()
        {
            // Every pre-5.6.0 member's 5.4.0 value, in declaration order, with Ad appended last —
            // one comparison pins both "Ad is last" and "nothing before it moved".
            var expected = new (SegmentKind Value, int Ordinal)[]
            {
                (SegmentKind.StationId, 0),
                (SegmentKind.LeadIn, 1),
                (SegmentKind.BackAnnounce, 2),
                (SegmentKind.TimeDate, 3),
                (SegmentKind.SignOff, 4),
                (SegmentKind.SignOn, 5),
                (SegmentKind.ContextSegment, 6),
                (SegmentKind.Crosstalk, 7),
                (SegmentKind.Announcement, 8),
                (SegmentKind.Ad, 9),
            };

            Assert.Equal(expected, Enum.GetValues<SegmentKind>().Select(value => (value, (int)value)));
        }

        [Fact]
        public void ImagingKindAdIsTheLastMemberAndPriorValuesHold()
        {
            var expected = new (ImagingKind Value, int Ordinal)[]
            {
                (ImagingKind.Liner, 0),
                (ImagingKind.StationId, 1),
                (ImagingKind.Jingle, 2),
                (ImagingKind.Promo, 3),
                (ImagingKind.Ad, 4),
            };

            Assert.Equal(expected, Enum.GetValues<ImagingKind>().Select(value => (value, (int)value)));
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the package laws hold over the new surface
    // ---------------------------------------------------------------------

    public sealed class ScenarioL4HoldsOverTheNewTypes
    {
        [Fact]
        public void DepsJsonCarriesNoLibraryEntryBeyondSelf()
        {
            // The real L4-references detector (Story290_DependencyLaws.cs's own
            // AbstractionsReferencesNothingBeyondTheBcl fact runs the identical call) — deps.json has
            // no per-type granularity, so "the new types added no reference" and "the rebuilt package
            // added no reference" are the same question at this law's grain.
            var extraLibraries = DepsJsonDependencyScan.ExtraLibrariesForProject(
                "src/GenWave.Abstractions", "GenWave.Abstractions");

            Assert.Empty(extraLibraries);
        }

        [Fact]
        public void EveryNewPublicTypeIsImmutable()
        {
            // The real L4-immutability detector (AbstractionsImmutability, the exact function
            // ScenarioL4Immutability in Story291_ConventionLaws.cs runs over the whole assembly),
            // scoped down to just the three new types this task adds.
            var subjects = new[]
            {
                ResolveAbstractionsType("GenWave.Core.Abstractions.IGenWavePlugin"),
                ResolveAbstractionsType("GenWave.Core.Abstractions.IPluginHost"),
                ResolveAbstractionsType("GenWave.Core.Abstractions.IAdSpotSource"),
            };

            var violations = AbstractionsImmutability.FindViolations(subjects);

            Assert.Empty(violations);
        }
    }

    // ---------------------------------------------------------------------
    // Shared reflection helpers
    // ---------------------------------------------------------------------

    /// <summary>Resolves <paramref name="fullName"/> against the real, loaded
    /// <c>GenWave.Abstractions</c> assembly (<see cref="ProductionAssemblies.Abstractions"/>), or
    /// throws — every name this file resolves names a type this same task adds, so a null result
    /// means the type is missing, never an expected outcome to fold into an assertion.</summary>
    private static Type ResolveAbstractionsType(string fullName) =>
        ProductionAssemblies.Abstractions.GetType(fullName)
            ?? throw new InvalidOperationException(
                $"\"{fullName}\" did not resolve against the loaded GenWave.Abstractions assembly.");

    /// <summary>A type's own declared public API, each member reduced to a short, orderable
    /// signature string — <c>"Name:PropertyTypeName"</c> for a property, <c>"Method(Param1,Param2):
    /// ReturnTypeName"</c> for a method — so an exact-member-set check is one array comparison instead
    /// of a growing pile of <c>Assert.Contains</c>/<c>Assert.DoesNotContain</c> calls that could never
    /// catch an UNEXPECTED extra member. <see cref="BindingFlags.Static"/> rides alongside
    /// <see cref="BindingFlags.Instance"/> so a future <c>static abstract</c> interface member (a
    /// legal C# shape this reflection call would otherwise walk straight past) still shows up here —
    /// without it, the "catches any extra member" claim above would be false for that one shape.
    /// Property accessor methods (<c>get_Name</c>) are compiler "special name" methods and are
    /// filtered out so a property contributes exactly one entry, not two or three.
    ///
    /// What this signature string does NOT capture: a property's nullability (<c>String</c> vs.
    /// <c>String?</c> both reduce to <c>"String"</c>) and whether it exposes a setter at all
    /// (get-only vs. get/set both reduce to the same entry). Those two shapes are the compiler's and
    /// the L4-immutability fact's (<see cref="AbstractionsImmutability"/>, exercised directly in
    /// <see cref="ScenarioL4HoldsOverTheNewTypes.EveryNewPublicTypeIsImmutable"/> above) to hold —
    /// this reflection helper only ever needed to prove the MEMBER SET, not every member's full
    /// shape.</summary>
    private static IReadOnlyList<string> PublicApiShape(Type type) =>
        type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member is not MethodInfo { IsSpecialName: true })
            .Select(DescribeMember)
            .OrderBy(description => description, StringComparer.Ordinal)
            .ToArray();

    private static string DescribeMember(MemberInfo member) => member switch
    {
        PropertyInfo property => $"{property.Name}:{property.PropertyType.Name}",
        MethodInfo method => $"{method.Name}(" +
            $"{string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.Name))}" +
            $"):{method.ReturnType.Name}",
        _ => throw new NotSupportedException(
            $"\"{member.Name}\" is a {member.MemberType} member — PublicApiShape only expects properties and methods."),
    };
}
