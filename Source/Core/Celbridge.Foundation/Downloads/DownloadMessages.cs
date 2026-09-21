namespace Celbridge.Downloads;

/// <summary>
/// Sent when the recorded downloads change. HasArrival is true when a download settled, whether it
/// succeeded or failed.
/// </summary>
public record DownloadsChangedMessage(bool HasArrival);
