using Celbridge.Commands;
using Celbridge.Core;
using Celbridge.Messaging;
using Celbridge.Utilities;
using CommunityToolkit.Diagnostics;
using CountlySDK.Entities;
using CountlySDK;
using Newtonsoft.Json;
using System.Text.Json.Nodes;

namespace Celbridge.Telemetry.Services;

public class TelemetryService : ITelemetryService
{
    private readonly IMessengerService _messengerService;
    private readonly IUtilityService _utiltyService;

    public TelemetryService(
        IMessengerService messengerService,
        IUtilityService utilityService)
    {
        _messengerService = messengerService;
        _utiltyService = utilityService;
    }

    public Result Initialize()
    {
        try
        {
            _messengerService.Register<CommandExecutingMessage>(this, OnExecutedCommandMessage);

            //create the Countly init object
            CountlyConfig cc = new CountlyConfig();
            cc.serverUrl = "https://celbridge-9d9f1a60360c3.flex.countly.com";

            // Todo: Inject this key as part of build process instead of hard coding it
            cc.appKey = "8b89bef9c197b87ad2b130f6bcee3512910a987e";
            cc.appVersion = _utiltyService.GetEnvironmentInfo().AppVersion;

            //Countly.IsLoggingEnabled = true;
            Countly.Instance.Init(cc);


        }
        catch (Exception ex )
        {
            return Result.Fail($"Failed to initialize telemetry service. {ex}");
        }

        return Result.Ok();
    }

    public Result RecordEvent(object? eventObject)
    {
        if (eventObject is null)
        {
            return Result.Ok();
        }

        var eventName = eventObject.GetType().Name;
        var eventJson = JsonConvert.SerializeObject(eventObject);

        _ = SendTelemetryEventAsync(eventName, eventJson);

        return Result.Ok();
    }

    private async Task SendTelemetryEventAsync(string eventName, string eventJson)
    {
        var jo = JsonObject.Parse(eventJson) as JsonObject;
        Guard.IsNotNull(jo);

        var segmentation = new Segmentation();
        foreach (var kv in jo)
        {
            var value = kv.Value is null ? string.Empty : kv.Value.ToString();
            segmentation.Add(kv.Key, value);
        }

        await Countly.RecordEvent(eventName, 1, segmentation);
    }

    private void OnExecutedCommandMessage(object recipient, CommandExecutingMessage message)
    {
        var segmentation = new Segmentation();
        segmentation.Add("CommandName", message.Command.GetType().Name);
        segmentation.Add("ExecutionMode", message.ExecutionMode.ToString());

        Countly.RecordEvent("ExecutedCommand", 1, segmentation);
    }
}