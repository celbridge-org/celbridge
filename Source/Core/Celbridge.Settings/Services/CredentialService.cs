using System.Text;
using System.Text.Json;
using Celbridge.Credentials;
using Celbridge.FileSystem;
using Celbridge.Logging;

namespace Celbridge.Settings.Services;

/// <summary>
/// On-disk shape of the credential store file: a version number and one
/// protected entry per credential. New credential types are added as nullable
/// entry properties without a version bump; a missing property deserializes as
/// null and reports as not configured. Adding a second entry requires changing
/// Set and Clear from whole-file rewrite and delete to read-modify-write.
/// Version increments only when the document is reshaped, paired with a
/// read-side migration.
/// </summary>
internal sealed record CredentialStoreDocument(int Version, WorkshopConnectionEntry? WorkshopConnection);

/// <summary>
/// Stored form of the Workshop connection: the protected connection payload as
/// base64, plus the unprotected key prefix used as a display hint.
/// </summary>
internal sealed record WorkshopConnectionEntry(string ProtectedData, string KeyHint);

/// <summary>
/// Stores credentials as platform-protected blobs in a JSON document in the
/// application data folder. All file access is serialized so concurrent
/// callers cannot interleave reads and writes.
/// </summary>
internal sealed class CredentialService : ICredentialService
{
    private const int StoreVersion = 1;

    private const string UnavailableMessage = "Credential storage is not available on this platform";
    private const string NotConfiguredMessage = "No Workshop connection is configured. Enter the Workshop URL and Application Key on the Settings page.";
    private const string CorruptStoreMessage = "The stored Workshop connection could not be read. Enter the Workshop URL and Application Key again on the Settings page.";
    private const string NewerStoreVersionMessage = "The credential store was written by a newer version of Celbridge and cannot be read by this version.";

    private static readonly JsonSerializerOptions DocumentJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ILogger<CredentialService> _logger;
    private readonly ILocalFileSystem _fileSystem;
    private readonly ICredentialProtector _protector;
    private readonly string _credentialsFilePath;
    private readonly SemaphoreSlim _storeSemaphore = new(1, 1);

    public CredentialService(
        ILogger<CredentialService> logger,
        ILocalFileSystem fileSystem,
        ICredentialProtector protector)
        : this(logger, fileSystem, protector, GetDefaultCredentialsFilePath())
    {}

    internal CredentialService(
        ILogger<CredentialService> logger,
        ILocalFileSystem fileSystem,
        ICredentialProtector protector,
        string credentialsFilePath)
    {
        _logger = logger;
        _fileSystem = fileSystem;
        _protector = protector;
        _credentialsFilePath = credentialsFilePath;
    }

    public bool IsAvailable => _protector.IsAvailable;

    public async Task<Result<WorkshopConnectionSummary>> GetWorkshopConnectionSummaryAsync()
    {
        if (!IsAvailable)
        {
            return Result.Fail(UnavailableMessage);
        }

        await _storeSemaphore.WaitAsync();
        try
        {
            var infoResult = await _fileSystem.GetInfoAsync(_credentialsFilePath);
            if (infoResult.IsFailure)
            {
                return Result<WorkshopConnectionSummary>.Fail("Failed to query the credential store file")
                    .WithErrors(infoResult);
            }

            var storeInfo = infoResult.Value;
            if (storeInfo.Kind != StorageItemKind.File)
            {
                return new WorkshopConnectionSummary(false, string.Empty);
            }

            var readResult = await _fileSystem.ReadAllTextAsync(_credentialsFilePath);
            if (readResult.IsFailure)
            {
                return Result<WorkshopConnectionSummary>.Fail("Failed to read the credential store file")
                    .WithErrors(readResult);
            }

            var documentText = readResult.Value;
            var document = ParseDocument(documentText);
            if (document is null)
            {
                // An unparseable store still counts as a stored entry so that
                // display surfaces can offer clear and replace as recovery.
                return new WorkshopConnectionSummary(true, string.Empty);
            }

            var entry = document.WorkshopConnection;
            if (entry is null ||
                string.IsNullOrEmpty(entry.ProtectedData))
            {
                return new WorkshopConnectionSummary(false, string.Empty);
            }

            return new WorkshopConnectionSummary(true, entry.KeyHint ?? string.Empty);
        }
        finally
        {
            _storeSemaphore.Release();
        }
    }

    public async Task<Result<WorkshopConnection>> GetWorkshopConnectionAsync()
    {
        if (!IsAvailable)
        {
            return Result.Fail(UnavailableMessage);
        }

        await _storeSemaphore.WaitAsync();
        try
        {
            var infoResult = await _fileSystem.GetInfoAsync(_credentialsFilePath);
            if (infoResult.IsFailure)
            {
                return Result<WorkshopConnection>.Fail("Failed to query the credential store file")
                    .WithErrors(infoResult);
            }

            var storeInfo = infoResult.Value;
            if (storeInfo.Kind != StorageItemKind.File)
            {
                return Result.Fail(NotConfiguredMessage);
            }

            var readResult = await _fileSystem.ReadAllTextAsync(_credentialsFilePath);
            if (readResult.IsFailure)
            {
                return Result<WorkshopConnection>.Fail("Failed to read the credential store file")
                    .WithErrors(readResult);
            }

            var documentText = readResult.Value;
            var document = ParseDocument(documentText);
            if (document is null)
            {
                return Result.Fail(CorruptStoreMessage);
            }

            if (document.Version > StoreVersion)
            {
                return Result.Fail(NewerStoreVersionMessage);
            }

            var entry = document.WorkshopConnection;
            if (entry is null ||
                string.IsNullOrEmpty(entry.ProtectedData))
            {
                return Result.Fail(NotConfiguredMessage);
            }

            byte[] protectedData;
            try
            {
                protectedData = Convert.FromBase64String(entry.ProtectedData);
            }
            catch (FormatException)
            {
                _logger.LogError("The stored Workshop connection entry is not valid base64");

                return Result.Fail(CorruptStoreMessage);
            }

            var unprotectResult = _protector.Unprotect(protectedData);
            if (unprotectResult.IsFailure)
            {
                _logger.LogError(unprotectResult, "Failed to unprotect the stored Workshop connection");

                return Result.Fail(CorruptStoreMessage);
            }

            var plainData = unprotectResult.Value;
            var connection = ParseConnectionPayload(plainData);
            if (connection is null ||
                string.IsNullOrEmpty(connection.WorkshopUrl) ||
                string.IsNullOrEmpty(connection.ApplicationKey))
            {
                // The decrypted payload is sensitive, so no parse detail is
                // attached to the error or written to the log.
                _logger.LogError("The stored Workshop connection payload is invalid");

                return Result.Fail(CorruptStoreMessage);
            }

            // Connections saved before the Author field was added have no value
            // for it; normalize the missing case to empty so callers gate on it
            // uniformly rather than guarding against null.
            if (connection.Author is null)
            {
                connection = connection with { Author = string.Empty };
            }

            return connection;
        }
        finally
        {
            _storeSemaphore.Release();
        }
    }

    public async Task<Result> SetWorkshopConnectionAsync(WorkshopConnection connection)
    {
        if (!IsAvailable)
        {
            return Result.Fail(UnavailableMessage);
        }

        if (connection is null)
        {
            return Result.Fail("Workshop connection is required");
        }

        if (string.IsNullOrWhiteSpace(connection.WorkshopUrl))
        {
            return Result.Fail("Workshop URL must not be empty");
        }

        if (string.IsNullOrWhiteSpace(connection.ApplicationKey))
        {
            return Result.Fail("Application Key must not be empty");
        }

        var payloadJson = JsonSerializer.Serialize(connection);
        var payloadData = Encoding.UTF8.GetBytes(payloadJson);

        var protectResult = _protector.Protect(payloadData);
        if (protectResult.IsFailure)
        {
            return Result.Fail("Failed to protect the Workshop connection")
                .WithErrors(protectResult);
        }

        var protectedData = protectResult.Value;
        var entry = new WorkshopConnectionEntry(
            Convert.ToBase64String(protectedData),
            GetKeyDisplayHint(connection.ApplicationKey));

        var document = new CredentialStoreDocument(StoreVersion, entry);
        var documentText = JsonSerializer.Serialize(document, DocumentJsonOptions);

        await _storeSemaphore.WaitAsync();
        try
        {
            var folderPath = Path.GetDirectoryName(_credentialsFilePath);
            if (!string.IsNullOrEmpty(folderPath))
            {
                var createFolderResult = await _fileSystem.CreateFolderAsync(folderPath);
                if (createFolderResult.IsFailure)
                {
                    return Result.Fail("Failed to create the credential store folder")
                        .WithErrors(createFolderResult);
                }
            }

            var writeResult = await _fileSystem.WriteAllTextAsync(_credentialsFilePath, documentText);
            if (writeResult.IsFailure)
            {
                return Result.Fail("Failed to write the credential store file")
                    .WithErrors(writeResult);
            }

            return Result.Ok();
        }
        finally
        {
            _storeSemaphore.Release();
        }
    }

    public async Task<Result> ClearWorkshopConnectionAsync()
    {
        if (!IsAvailable)
        {
            return Result.Fail(UnavailableMessage);
        }

        await _storeSemaphore.WaitAsync();
        try
        {
            var infoResult = await _fileSystem.GetInfoAsync(_credentialsFilePath);
            if (infoResult.IsFailure)
            {
                return Result.Fail("Failed to query the credential store file")
                    .WithErrors(infoResult);
            }

            var storeInfo = infoResult.Value;
            if (storeInfo.Kind != StorageItemKind.File)
            {
                return Result.Ok();
            }

            // The Workshop connection is the only entry today, so clearing it
            // removes the whole store file. This also recovers from a
            // corrupted file without needing to parse it.
            var deleteResult = await _fileSystem.DeleteFileAsync(_credentialsFilePath);
            if (deleteResult.IsFailure)
            {
                return Result.Fail("Failed to delete the credential store file")
                    .WithErrors(deleteResult);
            }

            return Result.Ok();
        }
        finally
        {
            _storeSemaphore.Release();
        }
    }

    private static string GetDefaultCredentialsFilePath()
    {
        var appDataFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(appDataFolderPath, "Celbridge", "credentials.json");
    }

    private static CredentialStoreDocument? ParseDocument(string documentText)
    {
        try
        {
            return JsonSerializer.Deserialize<CredentialStoreDocument>(documentText);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static WorkshopConnection? ParseConnectionPayload(byte[] plainData)
    {
        try
        {
            var payloadJson = Encoding.UTF8.GetString(plainData);

            return JsonSerializer.Deserialize<WorkshopConnection>(payloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the identifying prefix of an Application Key shaped like
    /// "kpf_(prefix)_(secret)", or an empty string when the key does not match
    /// that shape, so that no secret material can leak into the hint.
    /// </summary>
    private static string GetKeyDisplayHint(string applicationKey)
    {
        if (!applicationKey.StartsWith(CredentialConstants.ApplicationKeyPrefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var separatorIndex = applicationKey.IndexOf('_', CredentialConstants.ApplicationKeyPrefix.Length);
        if (separatorIndex < 0)
        {
            return string.Empty;
        }

        return applicationKey.Substring(0, separatorIndex);
    }
}
