namespace GenWave.Core.Abstractions;

/// <summary>
/// Marker seam (SPEC F72.1, STORY-195) distinguishing the booth log's own
/// <see cref="IStationEventSink"/> consumer from the container's single ambient
/// <see cref="IStationEventSink"/> binding. A composing host (GenWave.Host's
/// <c>CompositeStationEventSink</c>) resolves THIS to find the booth log specifically among
/// however many <see cref="IStationEventSink"/> consumers exist, without depending on
/// GenWave.MediaLibrary's internal <c>BoothLogWriter</c> type. Never resolve this seam to publish
/// an event — resolve the ambient <see cref="IStationEventSink"/> instead, exactly like every
/// other publisher in the app.
/// </summary>
public interface IBoothLogEventConsumer : IStationEventSink;
