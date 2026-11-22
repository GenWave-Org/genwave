// STORY-554 — Engines are a list (SPEC F219.6 amendment · PLAN T750a): the per-engine time limit is a live setting.

namespace GenWave.Host.Tests.Specs;

using System.Net;
using System.Net.Http.Json;

public sealed record BudgetEngineRow(string Name, string Profile, int RenderBudgetSeconds, int ConfigRenderBudgetSeconds);

public sealed record BudgetEnginesBody(string Active, List<BudgetEngineRow> Engines);

public static class FeatureEngineRenderBudgetsSetting
{
    static Task<HttpResponseMessage> PutBudgetsAsync(HttpClient client, string value) =>
        client.PutAsJsonAsync("/api/settings", new[] { new { key = "Tts:EngineRenderBudgets", value } });

    // chatterbox is registered with the default budget of the seeded kokoro (30) — no per-engine config of its own
    public sealed class ScenarioAValidMap : EngineSettingScenario
    {
        [Fact]
        public async Task ItIsAccepted() =>
            Assert.Equal(HttpStatusCode.OK, (await PutBudgetsAsync(Client, "{\"chatterbox\":45}")).StatusCode);
    }

    public sealed class ScenarioTheEngineNameIsWrittenInOtherCase : EngineSettingScenario
    {
        [Fact]
        public async Task ItIsAccepted() =>
            Assert.Equal(HttpStatusCode.OK, (await PutBudgetsAsync(Client, "{\"Chatterbox\":45}")).StatusCode);
    }

    public sealed class ScenarioABlankValue : EngineSettingScenario
    {
        [Fact]
        public async Task ItIsAccepted() =>
            Assert.Equal(HttpStatusCode.OK, (await PutBudgetsAsync(Client, "")).StatusCode);
    }

    public sealed class ScenarioTheEndpointShowsTheOverride : EngineSettingScenario
    {
        BudgetEnginesBody? body;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Assert.Equal(HttpStatusCode.OK, (await PutBudgetsAsync(Client, "{\"chatterbox\":45}")).StatusCode);
            body = await Client.GetFromJsonAsync<BudgetEnginesBody>("/api/voices/engines");
        }

        [Fact]
        public void TheOverriddenEngineShowsIt() =>
            Assert.Equal(45, Assert.Single(body?.Engines ?? [], e => e.Name == "chatterbox").RenderBudgetSeconds);

        [Fact]
        public void AnotherEngineShowsItsOwnValue() =>
            Assert.Equal(30, Assert.Single(body?.Engines ?? [], e => e.Name == "kokoro").RenderBudgetSeconds);
    }

    public sealed class ScenarioTheEndpointShowsTheEnginesOwnLimitBesideTheOverride : EngineSettingScenario
    {
        BudgetEnginesBody? body;

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            Assert.Equal(HttpStatusCode.OK, (await PutBudgetsAsync(Client, "{\"chatterbox\":45}")).StatusCode);
            body = await Client.GetFromJsonAsync<BudgetEnginesBody>("/api/voices/engines");
        }

        [Fact]
        public void TheConfigFieldIsNotTheOverride() =>
            Assert.Equal(30, Assert.Single(body?.Engines ?? [], e => e.Name == "chatterbox").ConfigRenderBudgetSeconds);
    }

    // Sad path
    public sealed class ScenarioABadMap : EngineSettingScenario
    {
        [Theory]
        [InlineData("{\"nope\":45}")]
        [InlineData("{\"chatterbox\":0}")]
        [InlineData("{\"chatterbox\":601}")]
        [InlineData("{\"chatterbox\":\"45\"}")]
        [InlineData("{\"chatterbox\":4.5}")]
        [InlineData("[45]")]
        [InlineData("45")]
        [InlineData("not json")]
        [InlineData("{\"chatterbox\":45,\"Chatterbox\":60}")]
        public async Task ItIsRefusedWith400(string value) =>
            Assert.Equal(HttpStatusCode.BadRequest, (await PutBudgetsAsync(Client, value)).StatusCode);
    }
}
