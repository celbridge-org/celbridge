namespace Celbridge.Telemetry;

/// <summary>
/// Records telemetry events to a remote server and to a local log file.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Initializes the telemetry service.
    /// A log file of all recorded telemetry events is stored in the specified folder.
    /// </summary>
    Result Initialize(string logFolderPath, int maxFilesToKeep);

    /// <summary>
    /// Records a telemetry event.
    /// Serializes a telemetry event object, sends it to the telemetry server and writes it to
    /// the telemetry log file.
    /// </summary>
    Result RecordEvent(object? eventObject);
}
