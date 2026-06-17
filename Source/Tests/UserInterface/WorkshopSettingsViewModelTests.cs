using Celbridge.Packages;
using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the WorkshopSettingsView view model. The Workshop Key
/// round-trips through the real CredentialService over fakes;
/// the non-secret URL and Author are ordinary settings, here a substitute
/// IEditorSettings that records set values.
/// </summary>
[TestFixture]
public class WorkshopSettingsViewModelTests
{
    private const string WorkshopUrl = "https://workshop.celbridge.org";
    private const string TestWorkshopKey = "kpf_abc123_supersecretvalue";

    private FakeCredentialProtector _protector = null!;
    private CredentialService _credentialService = null!;
    private IEditorSettings _editorSettings = null!;
    private IPackageApiClient _packageApiClient = null!;
    private WorkshopSettingsViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _editorSettings = Substitute.For<IEditorSettings>();
        // Initialize to empty so unseeded reads return "" rather than the
        // substitute's null default. The real EditorSettings returns "" too.
        _editorSettings.WorkshopUrl = string.Empty;
        _editorSettings.WorkshopAuthor = string.Empty;
        _editorSettings.WorkshopKeyProtected = string.Empty;
        _editorSettings.WorkshopKeyHint = string.Empty;

        _protector = new FakeCredentialProtector();
        _credentialService = new CredentialService(
            new NullLogger<CredentialService>(),
            _protector,
            _editorSettings);

        _packageApiClient = Substitute.For<IPackageApiClient>();
        SetConnectionCheckResult(success: true);

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new WorkshopSettingsViewModel(
            Substitute.For<ILogger<WorkshopSettingsViewModel>>(),
            _editorSettings,
            _credentialService,
            _packageApiClient,
            stringLocalizer);
    }

    // Stubs the lightweight list-packages probe the connection check uses.
    private void SetConnectionCheckResult(bool success)
    {
        var result = success
            ? Result<IReadOnlyList<RemotePackageSummary>>.Ok(new List<RemotePackageSummary>())
            : Result<IReadOnlyList<RemotePackageSummary>>.Fail("connection failed");
        _packageApiClient.ListPackagesAsync().Returns(Task.FromResult(result));
    }

    // Seeds a stored key (credential store) plus the non-secret URL and Author
    // (settings), as a configured connection would appear at startup.
    private async Task SeedStoredConnectionAsync(string author = "")
    {
        await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);
        _editorSettings.WorkshopUrl = WorkshopUrl;
        _editorSettings.WorkshopAuthor = author;
    }

    [Test]
    public async Task Initialize_NothingStored_ShowsKeyEntry()
    {
        await _viewModel.InitializeAsync();

        _viewModel.IsStoreAvailable.Should().BeTrue();
        _viewModel.IsKeyEntryVisible.Should().BeTrue();
        _viewModel.IsStoredKeyVisible.Should().BeFalse();
    }

    [Test]
    public async Task Initialize_StoreUnavailable_ShowsErrorAndDisablesEntry()
    {
        _protector.Available = false;

        await _viewModel.InitializeAsync();

        _viewModel.IsStoreAvailable.Should().BeFalse();
        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        _viewModel.IsKeyEntryVisible.Should().BeFalse();
    }

    [Test]
    public async Task Initialize_StoredKey_ShowsUrlAndKeyPrefix()
    {
        await SeedStoredConnectionAsync();

        await _viewModel.InitializeAsync();

        _viewModel.WorkshopUrl.Should().Be(WorkshopUrl);
        _viewModel.StoredKeyDisplay.Should().Be("kpf_abc123_...");
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        _viewModel.IsKeyEntryVisible.Should().BeFalse();
    }

    [Test]
    public async Task Initialize_StoredKey_PopulatesAuthorFromSettings()
    {
        await SeedStoredConnectionAsync(author: "Ada Lovelace");

        await _viewModel.InitializeAsync();

        _viewModel.Author.Should().Be("Ada Lovelace");
    }

    [Test]
    public async Task Initialize_StoredKeyMissingAuthor_ShowsWarning()
    {
        await SeedStoredConnectionAsync();

        await _viewModel.InitializeAsync();

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Warning);
    }

    [Test]
    public async Task Save_PersistsUrlAndAuthorToSettingsWithoutKey()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.Author = "Ada Lovelace";
        // No key entered.

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        // URL and Author persist as settings, independently of any stored key.
        _editorSettings.WorkshopUrl.Should().Be(WorkshopUrl);
        _editorSettings.WorkshopAuthor.Should().Be("Ada Lovelace");
        _editorSettings.WorkshopKeyProtected.Should().BeEmpty();
    }

    [Test]
    public async Task Save_NewKey_StoresKeyAndShowsStoredKey()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.WorkshopKey = TestWorkshopKey;
        _viewModel.Author = "Ada Lovelace";

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        _viewModel.StatusSeverity.Should().NotBe(StatusSeverity.Error);
        _viewModel.WorkshopKey.Should().BeEmpty("the entered key should not linger in the view model");
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        _viewModel.StoredKeyDisplay.Should().Be("kpf_abc123_...");

        var keyResult = await _credentialService.GetWorkshopKeyAsync();
        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Should().Be(TestWorkshopKey);

        _editorSettings.WorkshopUrl.Should().Be(WorkshopUrl);
    }

    [Test]
    public async Task Save_KeyWithoutExpectedPrefix_StillStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.WorkshopKey = "not-a-kpf-shaped-key";

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        _viewModel.StatusSeverity.Should().NotBe(StatusSeverity.Error, "the prefix check is a typo guard, not a gate");

        var keyResult = await _credentialService.GetWorkshopKeyAsync();
        keyResult.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task Save_NoKeyEntered_PromptsForKey()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        // Valid URL, no key.

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Informational);
        _editorSettings.WorkshopKeyProtected.Should().BeEmpty();
    }

    [Test]
    public async Task Save_EmptyUrl_ShowsError()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = string.Empty;

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
    }

    [Test]
    public async Task Save_InvalidUrl_ShowsError()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = "http://workshop.celbridge.org";

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
    }

    [Test]
    public async Task Save_WithEmptyAuthor_WarnsButStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.WorkshopKey = TestWorkshopKey;
        // Author left empty: the key saves, but publishing needs an author.

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Warning);

        var keyResult = await _credentialService.GetWorkshopKeyAsync();
        keyResult.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task Save_UrlChange_PersistsToSettingsAndKeepsKey()
    {
        await SeedStoredConnectionAsync(author: "Ada Lovelace");
        await _viewModel.InitializeAsync();

        var updatedUrl = "https://other.celbridge.org";
        _viewModel.WorkshopUrl = updatedUrl;

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        _viewModel.StatusSeverity.Should().NotBe(StatusSeverity.Error);
        _editorSettings.WorkshopUrl.Should().Be(updatedUrl);

        var keyResult = await _credentialService.GetWorkshopKeyAsync();
        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task CheckConnection_Success_ShowsConnected()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.WorkshopKey = TestWorkshopKey;
        _viewModel.Author = "Ada Lovelace";
        SetConnectionCheckResult(success: true);

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: true);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Success);
    }

    [Test]
    public async Task CheckConnection_FailureWithWellFormedKey_ShowsCheckFailed()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.WorkshopKey = TestWorkshopKey;
        SetConnectionCheckResult(success: false);

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: true);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        // The substitute localizer echoes the resource key as the message.
        _viewModel.StatusMessage.Should().Be("SettingsPage_ConnectionCheckFailed");
    }

    [Test]
    public async Task CheckConnection_FailureWithMalformedKey_ReportsKeyInvalid()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.WorkshopKey = "not-a-kpf-shaped-key";
        SetConnectionCheckResult(success: false);

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: true);

        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        _viewModel.StatusMessage.Should().Be("SettingsPage_InvalidWorkshopKey");
    }

    [Test]
    public async Task Clear_RemovesStoredKeyButKeepsUrlAndAuthor()
    {
        await SeedStoredConnectionAsync(author: "Ada Lovelace");
        await _viewModel.InitializeAsync();

        await _viewModel.ClearWorkshopKeyCommand.ExecuteAsync(null);

        // Only the secret is removed.
        _editorSettings.WorkshopKeyProtected.Should().BeEmpty();
        _viewModel.StoredKeyDisplay.Should().BeEmpty();
        _viewModel.WorkshopKey.Should().BeEmpty();
        _viewModel.IsKeyEntryVisible.Should().BeTrue();

        // The non-secret URL and Author are untouched, in the form and in settings.
        _viewModel.WorkshopUrl.Should().Be(WorkshopUrl);
        _viewModel.Author.Should().Be("Ada Lovelace");
        _editorSettings.WorkshopUrl.Should().Be(WorkshopUrl);
        _editorSettings.WorkshopAuthor.Should().Be("Ada Lovelace");
    }

    [Test]
    public async Task ReplaceThenCancel_RestoresStoredKeyDisplay()
    {
        await SeedStoredConnectionAsync();
        await _viewModel.InitializeAsync();

        _viewModel.ReplaceWorkshopKeyCommand.Execute(null);

        _viewModel.IsKeyEntryVisible.Should().BeTrue();
        _viewModel.IsStoredKeyVisible.Should().BeFalse();
        _viewModel.IsCancelReplaceVisible.Should().BeTrue();

        _viewModel.WorkshopKey = "kpf_new123_partiallyentered";
        _viewModel.CancelReplaceWorkshopKeyCommand.Execute(null);

        _viewModel.WorkshopKey.Should().BeEmpty();
        _viewModel.IsKeyEntryVisible.Should().BeFalse();
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
    }
}
