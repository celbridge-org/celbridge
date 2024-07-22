using Celbridge.Messaging;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.Legacy.Services;

public class SaveQueueUpdatedMessage
{
    public int PendingSaveCount { get; }

    public SaveQueueUpdatedMessage(int queuedSaveCount)
    {
        PendingSaveCount = queuedSaveCount;
        // Log.Information($"Save Queue Updated: {PendingSaveCount}");
    }
}

public interface ISaveData
{
    Task<Result> SaveAsync();
}

public interface ISaveDataService
{
    public void RequestSave(ISaveData saveData);
    public bool IsPendingSave(ISaveData saveData);
    public bool IsSaving { get; }
    public Task StartMonitoringAsync(double interval);
    public void StopMonitoring();
}

public class SaveDataService : ISaveDataService
{
    private readonly IMessengerService _messengerService;
    private readonly Queue<ISaveData> _pendingSaves = new ();
    private readonly CancellationTokenSource _cancellationToken = new ();
    private ISaveData? _activeSaveData;

    private bool _receivedRecentSaveRequest;

    public SaveDataService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void RequestSave(ISaveData saveData)
    {
        // Only enqueue the request if there's no similar request already pending.
        if (!_pendingSaves.Contains(saveData))
        {
            _pendingSaves.Enqueue(saveData);

            var message = new SaveQueueUpdatedMessage((int)_pendingSaves.Count);
            _messengerService.Send(message);
        }

        _receivedRecentSaveRequest = true;
    }

    public bool IsPendingSave(ISaveData saveData)
    {
        return _pendingSaves.Contains(saveData) || _activeSaveData == saveData;
    }

    public bool IsSaving => _pendingSaves.Count > 0 || _activeSaveData != null;

    public async Task StartMonitoringAsync(double interval)
    {
        while (!_cancellationToken.Token.IsCancellationRequested)
        {
            // Wait between saves, and if a save request comes in while waiting we wait again.
            // This ensures that we only save after a period of inactivity.
            do
            {
                await Task.Delay(TimeSpan.FromSeconds(interval));
                if (_receivedRecentSaveRequest)
                {
                    _receivedRecentSaveRequest = false;
                }
                else
                {
                    break;
                }
            } while (true);

            if (_pendingSaves.Count > 0)
            {
                _activeSaveData = _pendingSaves.Dequeue();
                var result = await _activeSaveData.SaveAsync();
                if (result.Failure)
                {
                    var error = result as ErrorResult;
                    Log.Error(error!.Message);
                }
                _activeSaveData = null;

                var message = new SaveQueueUpdatedMessage((int)_pendingSaves.Count);
                _messengerService.Send(message);
            }
        }
    }

    public void StopMonitoring()
    {
        _cancellationToken.Cancel();
    }
}
