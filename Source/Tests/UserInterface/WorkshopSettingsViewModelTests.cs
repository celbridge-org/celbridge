using Celbridge.Dialog;
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
/// round-trips through the real SettingsService over a reversing protector fake;
/// the secret is entered through a substitute IDialogService standing in for the
/// masked input dialog. The non-secret URL and Author are ordinary settings, read
/// back through the same service.
/// </summary>
[TestFixture]
public class WorkshopSettingsViewModelTests
{
    private const string WorkshopUrl = "https://workshop.celbridge.org";
    private const string TestWorkshopKey = "kpf_abc123_supersecretvalue";

    private FakeSettingsStore _settingsStore = null!;
    private FakeCredentialProtector _protector = null!;
    private SettingsService _settingsService = null!;
    private IPackageApiClient _packageApiClient = null!;
    private IDialogService _dialogService = null!;
    private WorkshopSettingsViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _settingsStore = new FakeSettingsStore();
        _protector = new FakeCredentialProtector();

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        _settingsService = new SettingsService(
            new NullLogger<SettingsService>(),
            _settingsStore,
            _protector,
            workspaceWrapper);

        _packageApiClient = Substitute.For<IPackageApiClient>();
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Connected);

        _dialogService = Substitute.For<IDialogService>();

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new WorkshopSettingsViewModel(
            Substitute.For<ILogger<WorkshopSettingsViewModel>>(),
            _settingsService,
            _packageApiClient,
            _dialogService,
            stringLocalizer);
    }

    // Stubs the connection probe outcome the view model classifies.
    private void SetConnectionCheckOutcome(ConnectionCheckOutcome outcome)
    {
        _packageApiClient.CheckConnectionAsync().Returns(Task.FromResult(outcome));
    }

    // Stubs the masked key dialog to return the given key, as if the user entered
    // it and pressed Save.
    private void SetKeyDialogResult(string key)
    {
        _dialogService.ShowSecretInputDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(Result<string>.Ok(key)));
    }

    // Stubs the masked key dialog as cancelled.
    private void SetKeyDialogCancelled()
    {
        _dialogService.ShowSecretInputDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(Result<string>.Fail("cancelled")));
    }

    // Stubs the removal confirmation dialog with the given answer.
    private void SetRemoveConfirmationResult(bool confirmed)
    {
        _dialogService.ShowConfirmationDialogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(Result<bool>.Ok(confirmed)));
    }

    // Seeds a stored key (Protected scope) plus the non-secret URL and Author, as
    // a configured connection would appear at startup.
    private void SeedStoredConnection(string author = "")
    {
        _settingsService.Set(SettingCatalog.Workshop.Key, TestWorkshopKey);
        _settingsService.Set(SettingCatalog.Workshop.KeyHint, WorkshopKey.GetDisplayHint(TestWorkshopKey));
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
        _protector.Available = false;

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

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

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
        SetKeyDialogResult(TestWorkshopKey);

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.StatusSeverity.Should().NotBe(StatusSeverity.Error);
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        _viewModel.StoredKeyDisplay.Should().Be("kpf_abc123_...");

        GetStoredKey().Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task ChangeKey_Cancelled_LeavesStoredKeyUntouched()
    {
        SeedStoredConnection();
        await _viewModel.InitializeAsync();
        SetKeyDialogCancelled();

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        GetStoredKey().Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task ChangeKey_KeyWithoutExpectedPrefix_StillStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        SetKeyDialogResult("not-a-kpf-shaped-key");

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.StatusSeverity.Should().NotBe(StatusSeverity.Error, "the prefix check is a typo guard, not a gate");

        IsKeyStored().Should().BeTrue();
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
        IsKeyStored().Should().BeFalse();
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
    public async Task ChangeKey_WithEmptyAuthor_WarnsButStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        SetKeyDialogResult(TestWorkshopKey);
        // Author left empty: the key saves, but publishing needs an author.

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

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
        SetKeyDialogResult(TestWorkshopKey);
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Connected);

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Success);
    }

    [Test]
    public async Task CheckConnection_Unauthorized_ReportsKeyRejected()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        SetKeyDialogResult(TestWorkshopKey);
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Unauthorized);

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

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
        SetKeyDialogResult(TestWorkshopKey);
        SetConnectionCheckOutcome(ConnectionCheckOutcome.Unreachable);

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        // Offline must not be reported as a bad key: the key is saved, and the
        // status is a soft warning rather than an error.
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Warning);
        _viewModel.StatusMessage.Should().Be("Settings_Workshop_ConnectionUnverified");
        _viewModel.IsStoredKeyVisible.Should().BeTrue();

        GetStoredKey().Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task Save_UrlChange_PersistsToSettingsAndKeepsKey()
    {
        SeedStoredConnection(author: "Ada Lovelace");
        await _viewModel.InitializeAsync();

        var updatedUrl = "https://other.celbridge.org";
        _viewModel.WorkshopUrl = updatedUrl;

        await _viewModel.SaveWorkshopConnectionAsync(checkConnection: false);

        _viewModel.StatusSeverity.Should().NotBe(StatusSeverity.Error);
        _settingsService.Get(SettingCatalog.Workshop.Url).Should().Be(updatedUrl);

        GetStoredKey().Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task Remove_Confirmed_RemovesStoredKeyButKeepsUrlAndAuthor()
    {
        SeedStoredConnection(author: "Ada Lovelace");
        await _viewModel.InitializeAsync();
        SetRemoveConfirmationResult(confirmed: true);

        await _viewModel.RemoveWorkshopKeyCommand.ExecuteAsync(null);

        // Only the secret is removed.
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
        SetRemoveConfirmationResult(confirmed: false);

        await _viewModel.RemoveWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        GetStoredKey().Should().Be(TestWorkshopKey);
    }
}
