using System.Text;
using Celbridge.Credentials;
using Celbridge.Logging;

namespace Celbridge.Settings.Services;

/// <summary>
/// Stores credentials as platform-protected ciphertext in user-scoped settings.
/// The encrypted bytes live in IEditorSettings (WorkshopKeyProtected) alongside
/// a non-secret display hint (WorkshopKeyHint); encryption is provided by the
/// ICredentialProtector. The settings backing is user-and-installation scoped
/// (Windows LocalSettings), so credentials never travel with a project folder.
/// </summary>
internal sealed class CredentialService : ICredentialService
{
    private const string UnavailableMessage = "Credential storage is not available on this platform";
    private const string NotConfiguredMessage = "No Workshop Key is configured. Enter it on the Settings page.";
    private const string UnreadableMessage = "A stored credential could not be read. Enter it again on the Settings page.";

    // Fixed entropy bound into the Workshop Key ciphertext. Not a secret in
    // itself; it scopes our protected blob so that a different DPAPI consumer
    // running as the same user cannot Unprotect it by accident. The "v1"
    // suffix is reserved for future rotation if we ever need to invalidate
    // every stored Workshop Key in one step.
    private static readonly byte[] WorkshopKeyEntropy = Encoding.UTF8.GetBytes("Celbridge.WorkshopKey.v1");

    private readonly ILogger<CredentialService> _logger;
    private readonly ICredentialProtector _protector;
    private readonly IEditorSettings _editorSettings;

    public CredentialService(
        ILogger<CredentialService> logger,
        ICredentialProtector protector,
        IEditorSettings editorSettings)
    {
        _logger = logger;
        _protector = protector;
        _editorSettings = editorSettings;
    }

    public bool IsAvailable => _protector.IsAvailable;

    public async Task<Result<WorkshopKeySummary>> GetWorkshopKeySummaryAsync()
    {
        await Task.CompletedTask;

        if (!IsAvailable)
        {
            return Result.Fail(UnavailableMessage);
        }

        var protectedData = _editorSettings.WorkshopKeyProtected;
        if (string.IsNullOrEmpty(protectedData))
        {
            return new WorkshopKeySummary(false, string.Empty);
        }

        return new WorkshopKeySummary(true, _editorSettings.WorkshopKeyHint ?? string.Empty);
    }

    public async Task<Result<string>> GetWorkshopKeyAsync()
    {
        await Task.CompletedTask;

        if (!IsAvailable)
        {
            return Result.Fail(UnavailableMessage);
        }

        var protectedData = _editorSettings.WorkshopKeyProtected;
        if (string.IsNullOrEmpty(protectedData))
        {
            return Result.Fail(NotConfiguredMessage);
        }

        var valueResult = UnprotectFromBase64(protectedData);
        if (valueResult.IsFailure)
        {
            return valueResult;
        }

        var workshopKey = valueResult.Value;
        if (string.IsNullOrEmpty(workshopKey))
        {
            _logger.LogError("The stored Workshop Key is empty");

            return Result.Fail(UnreadableMessage);
        }

        return workshopKey;
    }

    public async Task<Result> SetWorkshopKeyAsync(string workshopKey)
    {
        await Task.CompletedTask;

        if (!IsAvailable)
        {
            return Result.Fail(UnavailableMessage);
        }

        if (string.IsNullOrWhiteSpace(workshopKey))
        {
            return Result.Fail("Workshop Key must not be empty");
        }

        var protectResult = ProtectToBase64(workshopKey);
        if (protectResult.IsFailure)
        {
            return Result.Fail("Failed to protect the Workshop Key").WithErrors(protectResult);
        }

        // The ciphertext is written before the hint, so a crash between the two
        // writes leaves a usable key with a stale hint (cosmetic only) rather
        // than a stored hint pointing at no key.
        _editorSettings.WorkshopKeyProtected = protectResult.Value;
        _editorSettings.WorkshopKeyHint = GetKeyDisplayHint(workshopKey);

        return Result.Ok();
    }

    public async Task<Result> ClearWorkshopKeyAsync()
    {
        await Task.CompletedTask;

        if (!IsAvailable)
        {
            return Result.Fail(UnavailableMessage);
        }

        _editorSettings.WorkshopKeyProtected = string.Empty;
        _editorSettings.WorkshopKeyHint = string.Empty;

        return Result.Ok();
    }

    private Result<string> ProtectToBase64(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var protectResult = _protector.Protect(data, WorkshopKeyEntropy);
        if (protectResult.IsFailure)
        {
            return Result<string>.Fail(protectResult);
        }

        return Convert.ToBase64String(protectResult.Value);
    }

    private Result<string> UnprotectFromBase64(string base64)
    {
        byte[] protectedData;
        try
        {
            protectedData = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            _logger.LogError("A stored credential entry is not valid base64");

            return Result.Fail(UnreadableMessage);
        }

        var unprotectResult = _protector.Unprotect(protectedData, WorkshopKeyEntropy);
        if (unprotectResult.IsFailure)
        {
            _logger.LogError(unprotectResult, "Failed to unprotect a stored credential");

            return Result.Fail(UnreadableMessage);
        }

        return Encoding.UTF8.GetString(unprotectResult.Value);
    }

    /// <summary>
    /// Returns the identifying prefix of a Workshop Key shaped like
    /// "kpf_(prefix)_(secret)", or an empty string when the key does not match
    /// that shape, so that no secret material can leak into the hint.
    /// </summary>
    private static string GetKeyDisplayHint(string workshopKey)
    {
        if (!workshopKey.StartsWith(CredentialConstants.WorkshopKeyPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var separatorIndex = workshopKey.IndexOf('_', CredentialConstants.WorkshopKeyPrefix.Length);
        if (separatorIndex < 0)
        {
            return string.Empty;
        }

        return workshopKey.Substring(0, separatorIndex);
    }
}
