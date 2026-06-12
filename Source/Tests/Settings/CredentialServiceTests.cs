using System.Text.Json;
using Celbridge.Credentials;
using Celbridge.Settings.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;

namespace Celbridge.Tests.Settings;

/// <summary>
/// In-memory protector that reverses the payload bytes, so protected data
/// never matches the plaintext but round-trips exactly.
/// </summary>
public sealed class FakeCredentialProtector : ICredentialProtector
{
    public bool Available { get; set; } = true;

    public bool FailUnprotect { get; set; }

    public bool IsAvailable => Available;

    public Result<byte[]> Protect(byte[] plainData)
    {
        var protectedData = plainData.Reverse().ToArray();

        return protectedData;
    }

    public Result<byte[]> Unprotect(byte[] protectedData)
    {
        if (FailUnprotect)
        {
            return Result.Fail("Simulated unprotect failure");
        }

        var plainData = protectedData.Reverse().ToArray();

        return plainData;
    }
}

/// <summary>
/// Unit tests for CredentialService covering the Workshop connection
/// round-trip, clearing, missing and corrupted store files, and availability.
/// </summary>
[TestFixture]
public class CredentialServiceTests
{
    private const string CredentialsFilePath = @"C:\AppData\Celbridge\credentials.json";
    private const string WorkshopUrl = "https://workshop.celbridge.org";
    private const string ApplicationKey = "kpf_abc123_supersecretvalue";

    private FakeFileSystem _fileSystem = null!;
    private FakeCredentialProtector _protector = null!;
    private CredentialService _credentialService = null!;

    [SetUp]
    public void Setup()
    {
        _fileSystem = new FakeFileSystem();
        _protector = new FakeCredentialProtector();
        _credentialService = new CredentialService(
            new NullLogger<CredentialService>(),
            _fileSystem,
            _protector,
            CredentialsFilePath);
    }

    [Test]
    public void IsAvailable_MirrorsProtectorAvailability()
    {
        _protector.Available = true;
        _credentialService.IsAvailable.Should().BeTrue();

        _protector.Available = false;
        _credentialService.IsAvailable.Should().BeFalse();
    }

    [Test]
    public async Task AllOperations_FailWhenStoreIsUnavailable()
    {
        _protector.Available = false;
        var connection = new WorkshopConnection(WorkshopUrl, ApplicationKey);

        var summaryResult = await _credentialService.GetWorkshopConnectionSummaryAsync();
        var getResult = await _credentialService.GetWorkshopConnectionAsync();
        var setResult = await _credentialService.SetWorkshopConnectionAsync(connection);
        var clearResult = await _credentialService.ClearWorkshopConnectionAsync();

        summaryResult.IsFailure.Should().BeTrue();
        getResult.IsFailure.Should().BeTrue();
        setResult.IsFailure.Should().BeTrue();
        clearResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task GetSummary_NothingStored_ReportsNotStored()
    {
        var summaryResult = await _credentialService.GetWorkshopConnectionSummaryAsync();

        summaryResult.IsSuccess.Should().BeTrue();

        var summary = summaryResult.Value;
        summary.IsStored.Should().BeFalse();
        summary.KeyHint.Should().BeEmpty();
    }

    [Test]
    public async Task GetSummary_StoredConnection_ReportsKeyHint()
    {
        var connection = new WorkshopConnection(WorkshopUrl, ApplicationKey);
        await _credentialService.SetWorkshopConnectionAsync(connection);

        var summaryResult = await _credentialService.GetWorkshopConnectionSummaryAsync();

        summaryResult.IsSuccess.Should().BeTrue();

        var summary = summaryResult.Value;
        summary.IsStored.Should().BeTrue();
        summary.KeyHint.Should().Be("kpf_abc123");
    }

    [Test]
    public async Task GetSummary_CorruptDocument_ReportsStoredWithoutHint()
    {
        _fileSystem.SeedFile(CredentialsFilePath, "this is not json");

        var summaryResult = await _credentialService.GetWorkshopConnectionSummaryAsync();

        summaryResult.IsSuccess.Should().BeTrue();

        var summary = summaryResult.Value;
        summary.IsStored.Should().BeTrue();
        summary.KeyHint.Should().BeEmpty();
    }

    [Test]
    public async Task SetThenGet_RoundTripsConnection()
    {
        var connection = new WorkshopConnection(WorkshopUrl, ApplicationKey);

        var setResult = await _credentialService.SetWorkshopConnectionAsync(connection);
        setResult.IsSuccess.Should().BeTrue();

        var getResult = await _credentialService.GetWorkshopConnectionAsync();
        getResult.IsSuccess.Should().BeTrue();

        var storedConnection = getResult.Value;
        storedConnection.Should().Be(connection);
    }

    [Test]
    public async Task Set_WritesNoPlaintextToDisk()
    {
        var connection = new WorkshopConnection(WorkshopUrl, ApplicationKey);

        await _credentialService.SetWorkshopConnectionAsync(connection);

        var readResult = await _fileSystem.ReadAllTextAsync(CredentialsFilePath);
        var documentText = readResult.Value;
        documentText.Should().NotContain(WorkshopUrl);
        documentText.Should().NotContain(ApplicationKey);
    }

    [Test]
    public async Task Set_StoresKeyDisplayHint()
    {
        var connection = new WorkshopConnection(WorkshopUrl, ApplicationKey);

        await _credentialService.SetWorkshopConnectionAsync(connection);

        var entry = await ReadStoredEntryAsync();
        entry.KeyHint.Should().Be("kpf_abc123");
    }

    [Test]
    public async Task Set_KeyWithUnexpectedShape_StoresEmptyHint()
    {
        var connection = new WorkshopConnection(WorkshopUrl, "not-a-kpf-shaped-key");

        await _credentialService.SetWorkshopConnectionAsync(connection);

        var entry = await ReadStoredEntryAsync();
        entry.KeyHint.Should().BeEmpty();
    }

    [Test]
    public async Task Set_EmptyValues_Fail()
    {
        var emptyUrl = new WorkshopConnection(string.Empty, ApplicationKey);
        var emptyKey = new WorkshopConnection(WorkshopUrl, string.Empty);

        var emptyUrlResult = await _credentialService.SetWorkshopConnectionAsync(emptyUrl);
        var emptyKeyResult = await _credentialService.SetWorkshopConnectionAsync(emptyKey);

        emptyUrlResult.IsFailure.Should().BeTrue();
        emptyKeyResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Get_MissingFile_FailsWithActionableMessage()
    {
        var getResult = await _credentialService.GetWorkshopConnectionAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("No Workshop connection is configured");
        getResult.FirstErrorMessage.Should().Contain("Settings page");
    }

    [Test]
    public async Task Get_CorruptDocument_FailsCleanly()
    {
        _fileSystem.SeedFile(CredentialsFilePath, "this is not json");

        var getResult = await _credentialService.GetWorkshopConnectionAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("could not be read");
    }

    [Test]
    public async Task Get_CorruptProtectedBlob_FailsCleanly()
    {
        var documentText = """
            {
              "Version": 1,
              "WorkshopConnection": {
                "ProtectedData": "@@not-valid-base64@@",
                "KeyHint": "kpf_abc123"
              }
            }
            """;
        _fileSystem.SeedFile(CredentialsFilePath, documentText);

        var getResult = await _credentialService.GetWorkshopConnectionAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("could not be read");
    }

    [Test]
    public async Task Get_NewerStoreVersion_FailsCleanly()
    {
        var documentText = """
            {
              "Version": 2,
              "WorkshopConnection": {
                "ProtectedData": "AAAA",
                "KeyHint": ""
              }
            }
            """;
        _fileSystem.SeedFile(CredentialsFilePath, documentText);

        var getResult = await _credentialService.GetWorkshopConnectionAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("newer version");
    }

    [Test]
    public async Task Get_UnprotectFailure_FailsWithoutEchoingValues()
    {
        var connection = new WorkshopConnection(WorkshopUrl, ApplicationKey);
        await _credentialService.SetWorkshopConnectionAsync(connection);
        _protector.FailUnprotect = true;

        var getResult = await _credentialService.GetWorkshopConnectionAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.MessageChain.Should().NotContain(WorkshopUrl);
        getResult.MessageChain.Should().NotContain(ApplicationKey);
    }

    [Test]
    public async Task Get_TamperedPayload_FailsCleanly()
    {
        var garbagePayload = "garbage payload"u8.ToArray();
        var protectResult = _protector.Protect(garbagePayload);
        var protectedData = protectResult.Value;

        var entry = new WorkshopConnectionEntry(Convert.ToBase64String(protectedData), string.Empty);
        var document = new CredentialStoreDocument(1, entry);
        _fileSystem.SeedFile(CredentialsFilePath, JsonSerializer.Serialize(document));

        var getResult = await _credentialService.GetWorkshopConnectionAsync();

        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("could not be read");
    }

    [Test]
    public async Task Clear_RemovesStoredConnection()
    {
        var connection = new WorkshopConnection(WorkshopUrl, ApplicationKey);
        await _credentialService.SetWorkshopConnectionAsync(connection);

        var clearResult = await _credentialService.ClearWorkshopConnectionAsync();
        clearResult.IsSuccess.Should().BeTrue();

        _fileSystem.Files.Should().NotContainKey(CredentialsFilePath);

        var getResult = await _credentialService.GetWorkshopConnectionAsync();
        getResult.IsFailure.Should().BeTrue();
        getResult.FirstErrorMessage.Should().Contain("No Workshop connection is configured");
    }

    [Test]
    public async Task Clear_WhenNothingStored_Succeeds()
    {
        var clearResult = await _credentialService.ClearWorkshopConnectionAsync();

        clearResult.IsSuccess.Should().BeTrue();
    }

    private async Task<WorkshopConnectionEntry> ReadStoredEntryAsync()
    {
        var readResult = await _fileSystem.ReadAllTextAsync(CredentialsFilePath);
        var documentText = readResult.Value;
        var document = JsonSerializer.Deserialize<CredentialStoreDocument>(documentText);
        document.Should().NotBeNull();
        document!.WorkshopConnection.Should().NotBeNull();

        return document.WorkshopConnection!;
    }
}
