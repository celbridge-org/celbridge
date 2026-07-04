namespace Celbridge.Logging;

/// <summary>
/// Marks a command property whose value must not appear in logs. The command logger replaces the
/// value with a size summary, so user file content and clipboard text never leave the payload.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RedactedInLogsAttribute : Attribute
{
}
