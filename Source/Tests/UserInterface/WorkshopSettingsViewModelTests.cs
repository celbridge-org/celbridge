using Celbridge.Packages;
using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.Helpers;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the WorkshopSettingsView view model. The Workshop Key
/// round-trips through the real SettingsService over an in-memory credential store fake.
/// The secret is entered and the removal confirmed in place, so the tests drive the key row's
/// states directly. The non-secret URL and Author are ordinary settings, read back through the
/// same service.
/// </summary>
[TestFixture]
public class WorkshopSettingsViewModelTests
{
    private const string WorkshopUrl = "https://workshop.celbridge.org";
    private const string TestWorkshopKey = "kpf_abc123_supersecretvalue";

    private FakeSettingsStore _settingsStore = null!;
    private FakeCredentialStore _credentialStore = null!;
    private SettingsService _settingsService = null!;
    private IPackageApiClient _packageApiClient = null!;
    private WorkshopSettingsViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _settingsStore = new FakeSettingsStore();
        _credentialStore = new FakeCredentialStore();

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        _settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            _settingsStore,
            _credentialStore,
            workspaceWrapper);

        _packageApiClient = Substitute.For<IPackageApiClient>();
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Connected);

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new WorkshopSettingsViewModel(
            Substitute.For<ILogger<WorkshopSettingsViewModel>>(),
            _settingsService,
            _packageApiClient,
            stringLocalizer);
    }

    // Stubs the connection probe outcome the view model classifies.
    private void SetConnectionCheckOutcome(ConnectionCheckOutcome outcome)
    {
        _packageApiClient.CheckConnectionAsync().Returns(Task.FromResult(outcome));
    }

    // Opens the key entry field and types the given key into it, as the user would.
    private void EnterKey(string key)
    {
        _viewModel.BeginChangeWorkshopKeyCommand.Execute(null);
        _viewModel.KeyInput = key;
    }

    // Seeds a stored key (Protected scope) plus the non-secret URL and Author, as
    // a configured connection would appear at startup.
    private void SeedStoredConnection(string author = "")
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _settingsService.Set(SettingCatalog.Workshop.KeyHint, WorkshopKeyHelper.GetDisplayHint(TestWorkshopKey));
        _settingsService.Set(SettingCatalog.Workshop.Url, WorkshopUrl);
        _settingsService.Set(SettingCatalog.Workshop.Author, author);
    }

    private bool IsKeyStored()
    {
        return _settingsService.IsConfigured(SettingCatalog.Workshop.Key);
    }

    private string GetStoredKey()
    {
        var result = _settingsService.TryGet(SettingCatalog.Workshop.Key);
        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }

    [Test]
    public async Task Initialize_NothingStored_ShowsSetKey()
    {
        await _viewModel.InitializeAsync();

        _viewModel.IsStoreAvailable.Should().BeTrue();
        _viewModel.IsSetKeyVisible.Should().BeTrue();
        _viewModel.IsStoredKeyVisible.Should().BeFalse();
    }

    [Test]
    public async Task Initialize_StoreUnavailable_ShowsErrorAndDisablesEntry()
    {
        _credentialStore.Available = false;

        await _viewModel.InitializeAsync();

        _viewModel.IsStoreAvailable.Should().BeFalse();
        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        _viewModel.IsSetKeyVisible.Should().BeFalse();
    }

    [Test]
    public async Task Initialize_StoredKey_ShowsUrlAndKeyPrefix()
    {
        SeedStoredConnection();

        await _viewModel.InitializeAsync();

        _viewModel.WorkshopUrl.Should().Be(WorkshopUrl);
        _viewModel.StoredKeyDisplay.Should().Be("kpf_abc123_...");
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        _viewModel.IsSetKeyVisible.Should().BeFalse();
    }

    [Test]
    public async Task Initialize_StoredKey_PopulatesAuthorFromSettings()
    {
        SeedStoredConnection(author: "Ada Lovelace");

        await _viewModel.InitializeAsync();

        _viewModel.Author.Should().Be("Ada Lovelace");
    }

    [Test]
    public async Task Initialize_StoredKeyMissingAuthor_ShowsWarning()
    {
        SeedStoredConnection();

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

        _viewModel.SaveWorkshopConnection();

        // URL and Author persist as settings, independently of any stored key.
        _settingsService.Get(SettingCatalog.Workshop.Url).Should().Be(WorkshopUrl);
        _settingsService.Get(SettingCatalog.Workshop.Author).Should().Be("Ada Lovelace");
        IsKeyStored().Should().BeFalse();
    }

    [Test]
    public async Task ChangeKey_NewKey_StoresKeyAndShowsStoredKey()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.Author = "Ada Lovelace";
        EnterKey(TestWorkshopKey);

        await _viewModel.SaveWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.StatusSeverity.Should().NotBe(StatusSeverity.Error);
        _viewModel.IsKeyEditVisible.Should().BeFalse();
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        _viewModel.StoredKeyDisplay.Should().Be("kpf_abc123_...");

        GetStoredKey().Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task ChangeKey_Cancelled_LeavesStoredKeyUntouched()
    {
        SeedStoredConnection();
        await _viewModel.InitializeAsync();
        EnterKey("kpf_a_replacement_key");

        _viewModel.CancelChangeWorkshopKeyCommand.Execute(null);

        _viewModel.IsKeyEditVisible.Should().BeFalse();
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        GetStoredKey().Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task ChangeKey_BlankEntry_CannotBeSaved()
    {
        await _viewModel.InitializeAsync();
        EnterKey("   ");

        _viewModel.IsSaveKeyEnabled.Should().BeFalse();

        await _viewModel.SaveWorkshopKeyCommand.ExecuteAsync(null);

        IsKeyStored().Should().BeFalse();
    }

    [Test]
    public async Task ChangeKey_KeyWithoutExpectedPrefix_StillStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        EnterKey("not-a-kpf-shaped-key");

        await _viewModel.SaveWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.StatusSeverity.Should().NotBe(StatusSeverity.Error, "the prefix check is a typo guard, not a gate");

        IsKeyStored().Should().BeTrue();
    }

    [Test]
    public async Task TestConnection_NoKeyEntered_PromptsForKey()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        // Valid URL, no key.

        await _viewModel.TestConnectionCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Informational);
        IsKeyStored().Should().BeFalse();
    }

    [Test]
    public async Task TestConnection_EmptyUrl_ShowsError()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = string.Empty;

        await _viewModel.TestConnectionCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
    }

    [Test]
    public async Task TestConnection_InvalidUrl_ShowsError()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = "http://workshop.celbridge.org";

        await _viewModel.TestConnectionCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
    }

    [Test]
    public async Task ChangeKey_WithEmptyAuthor_WarnsButStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        EnterKey(TestWorkshopKey);
        // Author left empty: the key saves, but publishing needs an author.

        await _viewModel.SaveWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Warning);

        IsKeyStored().Should().BeTrue();
    }

    [Test]
    public async Task CheckConnection_Connected_ShowsConnected()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.Author = "Ada Lovelace";
        EnterKey(TestWorkshopKey);
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Connected);

        await _viewModel.SaveWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Success);
    }

    [Test]
    public async Task CheckConnection_Unauthorized_ReportsKeyRejected()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        EnterKey(TestWorkshopKey);
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Unauthorized);

        await _viewModel.SaveWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        // The substitute localizer echoes the resource key as the message.
        _viewModel.StatusMessage.Should().Be("Settings_Workshop_KeyRejected");
    }

    [Test]
    public async Task CheckConnection_Unreachable_SavesKeyButWarnsUnverified()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        EnterKey(TestWorkshopKey);
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Unreachable);

        await _viewModel.SaveWorkshopKeyCommand.ExecuteAsync(null);

        // Offline must not be reported as a bad key: the key is saved, and the
        // status is a soft warning rather than an error.
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Warning);
        _viewModel.StatusMessage.Should().Be("Settings_Workshop_ConnectionUnverified");
        _viewModel.IsStoredKeyVisible.Should().BeTrue();

        GetStoredKey().Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task TestConnection_WithStoredKey_ReportsVerified()
    {
        SeedStoredConnection(author: "Ada Lovelace");
        await _viewModel.InitializeAsync();
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Connected);

        await _viewModel.TestConnectionCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Success);
    }

    [Test]
    public async Task Save_UrlChange_PersistsToSettingsAndKeepsKey()
    {
        SeedStoredConnection(author: "Ada Lovelace");
        await _viewModel.InitializeAsync();

        var updatedUrl = "https://other.celbridge.org";
        _viewModel.WorkshopUrl = updatedUrl;

        _viewModel.SaveWorkshopConnection();

        // The auto-save persists silently, without reporting connection status.
        _viewModel.IsStatusVisible.Should().BeFalse();
        _settingsService.Get(SettingCatalog.Workshop.Url).Should().Be(updatedUrl);

        GetStoredKey().Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task Remove_Confirmed_RemovesStoredKeyButKeepsUrlAndAuthor()
    {
        SeedStoredConnection(author: "Ada Lovelace");
        await _viewModel.InitializeAsync();

        _viewModel.BeginRemoveWorkshopKeyCommand.Execute(null);
        _viewModel.IsRemoveConfirmVisible.Should().BeTrue();

        _viewModel.ConfirmRemoveWorkshopKeyCommand.Execute(null);

        // Only the secret is removed.
        _viewModel.IsRemoveConfirmVisible.Should().BeFalse();
        IsKeyStored().Should().BeFalse();
        _viewModel.StoredKeyDisplay.Should().BeEmpty();
        _viewModel.IsSetKeyVisible.Should().BeTrue();
        _viewModel.IsStoredKeyVisible.Should().BeFalse();

        // The non-secret URL and Author are untouched, in the form and in settings.
        _viewModel.WorkshopUrl.Should().Be(WorkshopUrl);
        _viewModel.Author.Should().Be("Ada Lovelace");
        _settingsService.Get(SettingCatalog.Workshop.Url).Should().Be(WorkshopUrl);
        _settingsService.Get(SettingCatalog.Workshop.Author).Should().Be("Ada Lovelace");
    }

    [Test]
    public async Task Remove_Cancelled_KeepsStoredKey()
    {
        SeedStoredConnection();
        await _viewModel.InitializeAsync();
        _viewModel.BeginRemoveWorkshopKeyCommand.Execute(null);

        _viewModel.CancelRemoveWorkshopKeyCommand.Execute(null);

        _viewModel.IsRemoveConfirmVisible.Should().BeFalse();
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        GetStoredKey().Should().Be(TestWorkshopKey);
    }
}
