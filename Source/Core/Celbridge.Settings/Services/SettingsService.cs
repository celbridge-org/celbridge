using System.Text;
using System.Text.Json;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Settings.Services;

/// <summary>
/// Routes typed setting reads and writes to the backend named by each
/// descriptor's scope: the application settings store for Application, the same
/// store through the credential protector for Protected, and the live per-project
/// store for Workspace. Reads are synchronous and write-through.
/// </summary>
internal sealed class SettingsService : ISettingsService
{
    private const string ProtectedUnavailableMessage = "Credential storage is not available on this platform";

    private readonly ILogger<SettingsService> _logger;
    private readonly ISettingsStore _applicationStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SettingsService(
        ILogger<SettingsService> logger,
        ISettingsStore applicationStore,
        ICredentialStore credentialStore,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _applicationStore = applicationStore;
        _credentialStore = credentialStore;
        _workspaceWrapper = workspaceWrapper;
    }

    public bool IsScopeAvailable(SettingScope scope)
    {
        switch (scope)
        {
            case SettingScope.Application:
                return true;

            case SettingScope.Protected:
                return _credentialStore.IsAvailable;

            case SettingScope.Workspace:
                return WorkspaceStore is not null;

            default:
                return false;
        }
    }

    public T Get<T>(SettingDescriptor<T> setting) where T : notnull
    {
        switch (setting.Scope)
        {
            case SettingScope.Application:
                return _applicationStore.TryGetValue<T>(setting.Key, out var applicationValue)
                    ? applicationValue
                    : setting.DefaultValue;

            case SettingScope.Workspace:
                var store = WorkspaceStore;
                if (store is null)
                {
                    return setting.DefaultValue;
                }
                return store.TryGetValue<T>(setting.Key, out var workspaceValue)
                    ? workspaceValue
                    : setting.DefaultValue;

            case SettingScope.Protected:
                var protectedResult = TryGetProtected(setting);
                return protectedResult.IsSuccess
                    ? protectedResult.Value
                    : setting.DefaultValue;

            default:
                return setting.DefaultValue;
        }
    }

    public void Set<T>(SettingDescriptor<T> setting, T value) where T : notnull
    {
        switch (setting.Scope)
        {
            case SettingScope.Application:
                _applicationStore.SetValue(setting.Key, value);
                break;

            case SettingScope.Workspace:
                var store = WorkspaceStore;
                if (store is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot set Workspace setting '{setting.Key}' because no workspace is loaded.");
                }
                store.SetValue(setting.Key, value);
                break;

            case SettingScope.Protected:
                SetProtected(setting, value);
                break;
        }
    }

    public bool IsConfigured<T>(SettingDescriptor<T> setting) where T : notnull
    {
        switch (setting.Scope)
        {
            case SettingScope.Application:
                return _applicationStore.ContainsKey(setting.Key);

            case SettingScope.Protected:
                // The credential store answers presence without retrieving or decrypting the secret.
                return _credentialStore.ContainsCredential(setting.Key);

            case SettingScope.Workspace:
                var store = WorkspaceStore;
                return store is not null
                    && store.ContainsKey(setting.Key);

            default:
                return false;
        }
    }

    public Result<T> TryGet<T>(SettingDescriptor<T> setting) where T : notnull
    {
        switch (setting.Scope)
        {
            case SettingScope.Application:
                return _applicationStore.TryGetValue<T>(setting.Key, out var applicationValue)
                    ? Result<T>.Ok(applicationValue)
                    : Result<T>.Ok(setting.DefaultValue);

            case SettingScope.Workspace:
                var store = WorkspaceStore;
                if (store is null)
                {
                    return Result<T>.Ok(setting.DefaultValue);
                }
                return store.TryGetValue<T>(setting.Key, out var workspaceValue)
                    ? Result<T>.Ok(workspaceValue)
                    : Result<T>.Ok(setting.DefaultValue);

            case SettingScope.Protected:
                return TryGetProtected(setting);

            default:
                return Result<T>.Ok(setting.DefaultValue);
        }
    }

    public void Reset(ISettingDescriptor setting)
    {
        switch (setting.Scope)
        {
            case SettingScope.Application:
                _applicationStore.RemoveValue(setting.Key);
                break;

            case SettingScope.Protected:
                _credentialStore.DeleteCredential(setting.Key);
                break;

            case SettingScope.Workspace:
                WorkspaceStore?.RemoveValue(setting.Key);
                break;
        }
    }

    public Task<Result> FlushAsync()
    {
        // The Workspace store is flushed through the workspace save path. The Application store also backs
        // the Windows credential store (DPAPI ciphertext), so flushing it persists those too. The macOS
        // Keychain writes are immediate and need no flush.
        return _applicationStore.FlushAsync();
    }

    // The live per-project settings store, or null when no workspace is present.
    private ISettingsStore? WorkspaceStore
    {
        get
        {
            if (!_workspaceWrapper.HasWorkspaceService)
            {
                return null;
            }

            return _workspaceWrapper.WorkspaceService.WorkspaceSettings.WorkspaceSettingsStore;
        }
    }

    private void SetProtected<T>(SettingDescriptor<T> setting, T value) where T : notnull
    {
        if (!_credentialStore.IsAvailable)
        {
            throw new InvalidOperationException(ProtectedUnavailableMessage);
        }

        var json = JsonSerializer.Serialize(value);
        var plainData = Encoding.UTF8.GetBytes(json);

        var storeResult = _credentialStore.StoreCredential(setting.Key, plainData);
        if (storeResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to store protected setting '{setting.Key}': {storeResult.FirstErrorMessage}");
        }
    }

    private Result<T> TryGetProtected<T>(SettingDescriptor<T> setting) where T : notnull
    {
        if (!_credentialStore.IsAvailable)
        {
            return Result<T>.Fail(ProtectedUnavailableMessage);
        }

        if (!_credentialStore.ContainsCredential(setting.Key))
        {
            return Result<T>.Fail($"No value is configured for '{setting.Key}'");
        }

        var retrieveResult = _credentialStore.RetrieveCredential(setting.Key);
        if (retrieveResult.IsFailure)
        {
            _logger.LogError(retrieveResult, "Failed to read protected setting '{Key}'", setting.Key);

            return Result<T>.Fail("A stored protected value could not be read")
                .WithErrors(retrieveResult);
        }

        var json = Encoding.UTF8.GetString(retrieveResult.Value);
        try
        {
            var value = JsonSerializer.Deserialize<T>(json);
            if (value is null)
            {
                return Result<T>.Fail("A stored protected value could not be read");
            }

            return Result<T>.Ok(value);
        }
        catch (JsonException)
        {
            return Result<T>.Fail("A stored protected value could not be read");
        }
    }
}
