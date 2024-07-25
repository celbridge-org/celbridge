using Celbridge.Core;

namespace Celbridge.Telemetry.Services;

public class TelemetryService : ITelemetryService
{
    private readonly TelemetryLogger _telemetryLogger;

    public TelemetryService(TelemetryLogger telemetryLogger)
    {
        _telemetryLogger = telemetryLogger;
    }

    public Result Initialize(string logFolderPath, int maxFilesToKeep)
    {
        return _telemetryLogger.Initialize(logFolderPath, maxFilesToKeep);
    }

    public Result RecordEvent(object? eventObject)
    { 
        return _telemetryLogger.WriteObject(eventObject);
    }
}