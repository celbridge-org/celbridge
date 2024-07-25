using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Messaging;
using CountlySDK.Entities;
using CountlySDK;
using Windows.Storage;
using Celbridge.Utilities;

namespace Celbridge.Telemetry.Services;

public class TelemetryService : ITelemetryService
{
    private const string LogFolderName = "Logs";
    private const int MaxFilesToKeep = 0; // Todo: Make this configurable via settings

    private readonly IMessengerService _messengerService;
    private readonly IUtilityService _utiltyService;
    private readonly TelemetryLogger _telemetryLogger;

    public TelemetryService(
        IMessengerService messengerService,
        IUtilityService utilityService,
        TelemetryLogger telemetryLogger)
    {
        _messengerService = messengerService;
        _utiltyService = utilityService;
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

            //create the Countly init object
            CountlyConfig cc = new CountlyConfig();
            cc.serverUrl = "https://celbridge-9d9f1a60360c3.flex.countly.com";
            cc.appKey = "8b89bef9c197b87ad2b130f6bcee3512910a987e";
            cc.appVersion = _utiltyService.GetEnvironmentInfo().AppVersion;

            Countly.IsLoggingEnabled = true;
            Countly.Instance.Init(cc);

            Countly.Instance.SessionBegin();
        }
        catch (Exception ex )
        {
            return Result.Fail($"Failed to initialize telemetry service. {ex}");
        }

        return Result.Ok();
    }


    public Result RecordEvent(object? eventObject)
    {
        async Task RecordEventAsync()
        {
            var eventName = eventObject!.GetType().Name;
            var result = await Countly.RecordEvent(eventName, 3);
        }

        _ = RecordEventAsync();

        return _telemetryLogger.WriteObject(eventObject);
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        RecordEvent(message);
    }
}