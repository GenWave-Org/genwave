// STORY-384 — The contract grows without breaking anyone (F157.1, F158.1 · pending T390)

namespace GenWave.Architecture.Tests.Specs;

public static class FeatureAbstractionsContractGrows
{
    // ---------------------------------------------------------------------
    // HAPPY PATH
    // ---------------------------------------------------------------------

    public sealed class ScenarioThePluginSpiExistsAndIsMinimal
    {
        [Fact(Skip = "Pending T390 — see docs/PLAN.md")]
        public void IGenWavePluginExposesExactlyNameAndRegister()
        {
            // Reflect GenWave.Abstractions: IGenWavePlugin members ==
            //   { string Name { get; }, void Register(IPluginHost) } and nothing else.
            Assert.Fail("pending T390");
        }

        [Fact(Skip = "Pending T390 — see docs/PLAN.md")]
        public void IPluginHostExposesExactlyTheThreeAddAndSettingMembers()
        {
            // Members == { AddContextProvider(IContextProvider), AddAdSpotSource(IAdSpotSource),
            //              string? Setting(string) } — additive-only BY CONSTRUCTION (F156.5).
            Assert.Fail("pending T390");
        }
    }

    public sealed class ScenarioTheAdsSeamExists
    {
        [Fact(Skip = "Pending T390 — see docs/PLAN.md")]
        public void IAdSpotSourceExposesExactlyGetNextSpotAsync()
        {
            // Single member: ValueTask<MediaItem?> GetNextSpotAsync(CancellationToken).
            Assert.Fail("pending T390");
        }
    }

    public sealed class ScenarioTheEnumsAppendNeverReorder
    {
        [Fact(Skip = "Pending T390 — see docs/PLAN.md")]
        public void SegmentKindAdIsTheLastMemberAndPriorValuesHold()
        {
            // Enum.GetValues<SegmentKind>(): Ad is last; Announcement keeps its 5.4.0 value.
            Assert.Fail("pending T390");
        }

        [Fact(Skip = "Pending T390 — see docs/PLAN.md")]
        public void ImagingKindAdIsTheLastMemberAndPriorValuesHold()
        {
            // Enum.GetValues<ImagingKind>(): Ad is last; Liner/StationId/Jingle/Promo keep values.
            Assert.Fail("pending T390");
        }
    }

    // ---------------------------------------------------------------------
    // SAD PATH — the package laws hold over the new surface
    // ---------------------------------------------------------------------

    public sealed class ScenarioL4HoldsOverTheNewTypes
    {
        [Fact(Skip = "Pending T390 — see docs/PLAN.md")]
        public void DepsJsonCarriesNoLibraryEntryBeyondSelf()
        {
            // DepsJsonDependencyScan over the rebuilt package: the SPI added no reference
            //   (no IServiceCollection, no Microsoft.Extensions.* — F157.1).
            Assert.Fail("pending T390");
        }

        [Fact(Skip = "Pending T390 — see docs/PLAN.md")]
        public void EveryNewPublicTypeIsImmutable()
        {
            // AbstractionsImmutability over IGenWavePlugin/IPluginHost/IAdSpotSource.
            Assert.Fail("pending T390");
        }
    }
}
