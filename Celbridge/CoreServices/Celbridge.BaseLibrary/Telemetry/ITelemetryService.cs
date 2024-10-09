using Celbridge.Foundation;

namespace Celbridge.Telemetry;

/// <summary>
/// Records telemetry events to a remote server and to a local log file.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Initializes the telemetry service.
    /// </summary>
    Result Initialize();

    /// <summary>
    /// Records a telemetry event.
    /// Serializes a telemetry event object, sends it to the telemetry server and writes it to
    /// the telemetry log file.
    /// </summary>
    Result RecordEvent(object? eventObject);
}
