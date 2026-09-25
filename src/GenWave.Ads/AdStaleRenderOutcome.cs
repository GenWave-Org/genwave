namespace GenWave.Ads;

/// <summary>What <see cref="AdRenderService.RenderStaleAsync"/> actually did (gh-#854) — no
/// ClaimConflict case since the stale path can never reach Failed on the spot's own row, and no swap
/// payload either: <c>IAdSpotStore.SwapRenderedMediaAsync</c> commits the new media id, the spot's own
/// row, and both durable pending-eligibility markers together, in one guarded transaction; the two
/// eligibility flips themselves run best-effort AFTER that commit (this project has no grant into the
/// library schema, so they can never share the transaction), so nothing downstream of this outcome
/// ever needs to know which id won or reconcile anything itself — a failed flip stays pending for a
/// later drain, never surfaced here.</summary>
public abstract record AdStaleRenderOutcome
{
    /// <summary>The guarded swap committed — the new media row is now current and (best-effort)
    /// eligible; the old one is (best-effort) retired. <see cref="AdSpotWorker"/> treats this spot as
    /// no longer stale — nothing left to do.</summary>
    public sealed record Swapped : AdStaleRenderOutcome
    {
        public static readonly Swapped Instance = new();
    }

    /// <summary>The re-render failed, or its guarded swap declined; the spot's own row was never touched.</summary>
    public sealed record Failed : AdStaleRenderOutcome
    {
        public static readonly Failed Instance = new();
    }
}
