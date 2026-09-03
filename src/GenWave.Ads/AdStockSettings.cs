namespace GenWave.Ads;

/// <summary>
/// The three <c>Station:Ads:*</c> Live knobs <c>AdSpotWorker</c>'s own stock pass needs on every tick
/// (SPEC F159.3, F159.4; STORY-389; PLAN T402) — <see cref="AdStockSettingsReader.Read"/>'s own return
/// shape. Deliberately NOT a bound options class (see that reader's own remarks for why these three
/// specifically stay raw <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> reads, the
/// split <c>StationAdsOptions</c>' own PLAN T397 remarks already document).
/// </summary>
/// <param name="TargetCount">SPEC F159.3's <c>Station:Ads:TargetCount</c> — how many ready
/// llm/pack spots the stock pass holds the library at. Default 12.</param>
/// <param name="RefreshDays">SPEC F159.3's <c>Station:Ads:RefreshDays</c> — a ready spot older than
/// this many days retires. Default 30.</param>
/// <param name="AutoApprove">SPEC F159.4's <c>Station:Ads:AutoApprove</c> — whether a freshly
/// generated spot lands <c>approved</c> (render-eligible immediately) instead of <c>draft</c> (awaits
/// the operator). Default <see langword="false"/>.</param>
public sealed record AdStockSettings(int TargetCount, int RefreshDays, bool AutoApprove);
