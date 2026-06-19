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
    private readonly ICredentialProtector _protector;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SettingsService(
        ILogger<SettingsService> logger,
        ISettingsStore applicationStore,
        ICredentialProtector protector,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _applicationStore = applicationStore;
        _protector = protector;
        _workspaceWrapper = workspaceWrapper;
    }

    public bool IsScopeAvailable(SettingScope scope)
    {
        switch (scope)
        {
            case SettingScope.Application:
                return true;

            case SettingScope.Protected:
                return _protector.IsAvailable;

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
            case SettingScope.Protected:
                // Protected ciphertext lives in the application store, so a
                // presence check never has to decrypt.
                return _applicationStore.ContainsKey(setting.Key);

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
            case SettingScope.Protected:
                _applicationStore.RemoveValue(setting.Key);
                break;

            case SettingScope.Workspace:
                WorkspaceStore?.RemoveValue(setting.Key);
                break;
        }
    }

    public Task<Result> FlushAsync()
    {
        // The Workspace store is flushed through the workspace save path, so only
        // the Application store (which also holds the Protected ciphertext) needs
        // flushing here.
        return _applicationStore.FlushAsync();
    }

    // The live per-project store, or null when no workspace is loaded. Resolved
    // at call time through the workspace hub rather than injected, since the
    // store's lifetime is the loaded project, not the application.
    private ISettingsStore? WorkspaceStore
    {
        get
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return null;
            }

            return _workspaceWrapper.WorkspaceService.WorkspaceSettings.WorkspaceSettingsStore;
        }
    }

    private void SetProtected<T>(SettingDescriptor<T> setting, T value) where T : notnull
    {
        if (!_protector.IsAvailable)
        {
            throw new InvalidOperationException(ProtectedUnavailableMessage);
        }

        var json = JsonSerializer.Serialize(value);
        var plainData = Encoding.UTF8.GetBytes(json);
        var entropy = GetEntropy(setting.Key);

        var protectResult = _protector.Protect(plainData, entropy);
        if (protectResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Failed to protect setting '{setting.Key}': {protectResult.FirstErrorMessage}");
        }

        var base64 = Convert.ToBase64String(protectResult.Value);
        _applicationStore.SetValue(setting.Key, base64);
    }

    private Result<T> TryGetProtected<T>(SettingDescriptor<T> setting) where T : notnull
    {
        if (!_protector.IsAvailable)
        {
            return Result<T>.Fail(ProtectedUnavailableMessage);
        }

        if (!_applicationStore.TryGetValue<string>(setting.Key, out var base64)
            || string.IsNullOrEmpty(base64))
        {
            return Result<T>.Fail($"No value is configured for '{setting.Key}'");
        }

        byte[] protectedData;
        try
        {
            protectedData = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            _logger.LogError($"A stored protected value for '{setting.Key}' is not valid base64");

            return Result<T>.Fail("A stored protected value could not be read");
        }

        var entropy = GetEntropy(setting.Key);
        var unprotectResult = _protector.Unprotect(protectedData, entropy);
        if (unprotectResult.IsFailure)
        {
            _logger.LogError(unprotectResult, $"Failed to unprotect setting '{setting.Key}'");

            return Result<T>.Fail("A stored protected value could not be read");
        }

        var json = Encoding.UTF8.GetString(unprotectResult.Value);
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

    // The descriptor key is the DPAPI entropy, so each Protected setting has its
    // own entropy and rotating a key is a matter of renaming the descriptor.
    private static byte[] GetEntropy(string key)
    {
        return Encoding.UTF8.GetBytes(key);
    }
}
