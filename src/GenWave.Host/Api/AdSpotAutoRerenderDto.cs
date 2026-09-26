namespace GenWave.Host.Api;

/// <summary>
/// <see cref="AdSpotDto.AutoRerender"/>'s wire shape (gh-#865) — present only when the ad worker's
/// stale pass swapped this spot's take and no operator-driven render has happened since. The ad page
/// reads it as "Re-rendered automatically on <see cref="OnVersion"/>".
/// </summary>
/// <param name="OnVersion">The GenWave release the swap ran on, e.g. <c>v5.13.1</c>.</param>
/// <param name="At">When the swap landed.</param>
public sealed record AdSpotAutoRerenderDto(string OnVersion, DateTime At);
