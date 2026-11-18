// STORY-558 — DJs can laugh, the render path (SPEC F219 · PLAN F219)
//
// BDD specification — xUnit. Pending: each fact is activated by the task its Skip names.

namespace GenWave.Host.Tests.Specs;

public static class FeatureDjsCanLaughOnTheRenderPath
{
    // a DJ line with two tags through the preview endpoint, cap 1
    public sealed class ScenarioAPreviewOnChatterbox
    {
        [Fact(Skip = "pending: T761")]
        public void TheEngineReceivesOneTag() { }
    }

    // Sad path
    // AC6, AC7
    public sealed class ScenarioWhereTagsNeverGo
    {
        [Fact(Skip = "pending: T761")]
        public void TheKokoroEngineReceivesNoTag() { }

        [Fact(Skip = "pending: T761")]
        public void AnAdRenderInputHasNoTags() { }
    }
}
