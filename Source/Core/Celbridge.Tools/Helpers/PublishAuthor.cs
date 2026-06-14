using Celbridge.Credentials;

namespace Celbridge.Tools;

/// <summary>
/// Resolves the publisher Author from the stored Workshop connection. Every
/// publish records who published it; until a user-account login exists the
/// Author is set once on the Settings page. Returns a clear, actionable failure
/// when no connection or no Author is configured, so the publish tools can both
/// surface it to the agent and alert the user.
/// </summary>
internal static class PublishAuthor
{
    public static async Task<Result<string>> ResolveAsync(ICredentialService credentialService)
    {
        var connectionResult = await credentialService.GetWorkshopConnectionAsync();
        if (connectionResult.IsFailure)
        {
            return Result<string>.Fail(
                "Cannot publish: no workshop connection is configured. Set it up on the Settings page, then try again.")
                .WithErrors(connectionResult);
        }
        var connection = connectionResult.Value;

        if (string.IsNullOrWhiteSpace(connection.Author))
        {
            return Result<string>.Fail(
                "Cannot publish: no Author is set. Add one on the Settings page, then try again.");
        }

        return connection.Author.Trim();
    }
}
