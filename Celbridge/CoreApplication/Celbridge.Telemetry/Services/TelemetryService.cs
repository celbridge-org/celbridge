using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Messaging;
using Windows.Storage;

namespace Celbridge.Telemetry.Services;

public class TelemetryService : ITelemetryService
{
    private const string LogFolderName = "Logs";
    private const int MaxFilesToKeep = 0; // Todo: Make this configurable via settings

    private IMessengerService _messengerService;
    private readonly TelemetryLogger _telemetryLogger;

    public TelemetryService(
        IMessengerService messengerService,
        TelemetryLogger telemetryLogger)
    {
        _messengerService = messengerService;
        _telemetryLogger = telemetryLogger;
    }

    public Result Initialize()
    {
        try
        {
            string logFolderPath;
            StorageFolder folder = ApplicationData.Current.LocalFolder;
            logFolderPath = Path.Combine(folder.Path, LogFolderName);
            Directory.CreateDirectory(logFolderPath);

            var initResult = _telemetryLogger.Initialize(logFolderPath, MaxFilesToKeep);
            if (initResult.IsFailure)
            {
                return initResult;
            }

            _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);
        }
        catch (Exception ex )
        {
            return Result.Fail($"Failed to initialize telemetry service. {ex}");
        }

        return Result.Ok();
    }


    public Result RecordEvent(object? eventObject)
    { 
        return _telemetryLogger.WriteObject(eventObject);
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        RecordEvent(message);
    }
}