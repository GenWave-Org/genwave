using GenWave.Core.Domain;

namespace GenWave.Host.Crosstalk;

/// <summary>
/// One tick's worth of "is a generation attempt even worth starting" facts (SPEC F127.7, PLAN
/// T286) — see <see cref="CrosstalkStockWorker.DecideAttempt"/>'s own remarks for exactly what
/// gates a tick must clear before this is produced.
/// </summary>
internal sealed record CrosstalkStockAttempt(string ShowSlug, ScheduleSegment HostBlock, ShowSummary Show);
