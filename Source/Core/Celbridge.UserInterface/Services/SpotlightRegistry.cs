using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

public sealed class SpotlightRegistry : ISpotlightRegistry
{
    private readonly Dictionary<string, LandmarkDescriptor> _landmarks = new();

    public void RegisterLandmark(LandmarkDescriptor landmark)
    {
        _landmarks[landmark.Id] = landmark;
    }

    public void UnregisterLandmark(string landmarkId)
    {
        _landmarks.Remove(landmarkId);
    }

    public IReadOnlyList<LandmarkDescriptor> GetLandmarks()
    {
        return _landmarks.Values.ToList();
    }

    public bool TryGetLandmark(string landmarkId, out LandmarkDescriptor? landmark)
    {
        if (_landmarks.TryGetValue(landmarkId, out var found))
        {
            landmark = found;
            return true;
        }

        landmark = null;
        return false;
    }
}
