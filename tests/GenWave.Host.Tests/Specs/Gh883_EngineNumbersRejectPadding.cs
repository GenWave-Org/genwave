// gh-#883 — the settings validator accepted "3 " for an engine number, but Liquidsoap's
// float_of_string rejects it, so the engine silently kept its default. A padded number is now refused.
//
// BDD specification — xUnit. Drives SettingValidator.Validate directly.

using GenWave.Host.Configuration;
using Microsoft.Extensions.Configuration;

namespace GenWave.Host.Tests.Specs;

public static class FeatureEngineNumbersRejectPadding
{
    static SettingValidator Validator() => new(new ConfigurationBuilder().Build());

    public sealed class ScenarioAPaddedNumberIsRefused
    {
        [Theory]
        [InlineData("GW_XFADE_MIN", "3 ")]
        [InlineData("GW_XFADE_MAX", " 5")]
        [InlineData("GW_SAFE_GAP_SECONDS", "\t2")]
        public void TheValidatorNamesAnError(string key, string value) =>
            Assert.NotNull(Validator().Validate(key, value));
    }

    public sealed class ScenarioAPlainNumberStillPasses
    {
        [Theory]
        [InlineData("GW_XFADE_MIN", "3")]
        [InlineData("GW_XFADE_MAX", "5.5")]
        [InlineData("GW_SAFE_GAP_SECONDS", "2")]
        public void TheValidatorAcceptsIt(string key, string value) =>
            Assert.Null(Validator().Validate(key, value));
    }
}
