using Celbridge.Dialog;
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
/// round-trips through the real CredentialService over fakes; the secret is
/// entered through a substitute IDialogService standing in for the masked
/// input dialog. The non-secret URL and Author are ordinary settings, here a
/// substitute IEditorSettings that records set values.
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
    private IDialogService _dialogService = null!;
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

        _dialogService = Substitute.For<IDialogService>();

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new WorkshopSettingsViewModel(
            Substitute.For<ILogger<WorkshopSettingsViewModel>>(),
            _editorSettings,
            _credentialService,
            _packageApiClient,
            _dialogService,
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

    // Seeds a stored key (credential store) plus the non-secret URL and Author
    // (settings), as a configured connection would appear at startup.
    private async Task SeedStoredConnectionAsync(string author = "")
    {
        await _credentialService.SetWorkshopKeyAsync(TestWorkshopKey);
        _editorSettings.WorkshopUrl = WorkshopUrl;
        _editorSettings.WorkshopAuthor = author;
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
        await SeedStoredConnectionAsync();

        await _viewModel.InitializeAsync();

        _viewModel.WorkshopUrl.Should().Be(WorkshopUrl);
        _viewModel.StoredKeyDisplay.Should().Be("kpf_abc123_...");
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        _viewModel.IsSetKeyVisible.Should().BeFalse();
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

        var keyResult = await _credentialService.GetWorkshopKeyAsync();
        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task ChangeKey_Cancelled_LeavesStoredKeyUntouched()
    {
        await SeedStoredConnectionAsync();
        await _viewModel.InitializeAsync();
        SetKeyDialogCancelled();

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStoredKeyVisible.Should().BeTrue();

        var keyResult = await _credentialService.GetWorkshopKeyAsync();
        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Should().Be(TestWorkshopKey);
    }

    [Test]
    public async Task ChangeKey_KeyWithoutExpectedPrefix_StillStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        SetKeyDialogResult("not-a-kpf-shaped-key");

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

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
    public async Task ChangeKey_WithEmptyAuthor_WarnsButStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        SetKeyDialogResult(TestWorkshopKey);
        // Author left empty: the key saves, but publishing needs an author.

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Warning);

        var keyResult = await _credentialService.GetWorkshopKeyAsync();
        keyResult.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task CheckConnection_Success_ShowsConnected()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.Author = "Ada Lovelace";
        SetKeyDialogResult(TestWorkshopKey);
        SetConnectionCheckResult(success: true);

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Success);
    }

    [Test]
    public async Task CheckConnection_FailureWithWellFormedKey_ShowsCheckFailed()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        SetKeyDialogResult(TestWorkshopKey);
        SetConnectionCheckResult(success: false);

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

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
        SetKeyDialogResult("not-a-kpf-shaped-key");
        SetConnectionCheckResult(success: false);

        await _viewModel.ChangeWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        _viewModel.StatusMessage.Should().Be("SettingsPage_InvalidWorkshopKey");
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
    public async Task Remove_Confirmed_RemovesStoredKeyButKeepsUrlAndAuthor()
    {
        await SeedStoredConnectionAsync(author: "Ada Lovelace");
        await _viewModel.InitializeAsync();
        SetRemoveConfirmationResult(confirmed: true);

        await _viewModel.RemoveWorkshopKeyCommand.ExecuteAsync(null);

        // Only the secret is removed.
        _editorSettings.WorkshopKeyProtected.Should().BeEmpty();
        _viewModel.StoredKeyDisplay.Should().BeEmpty();
        _viewModel.IsSetKeyVisible.Should().BeTrue();
        _viewModel.IsStoredKeyVisible.Should().BeFalse();

        // The non-secret URL and Author are untouched, in the form and in settings.
        _viewModel.WorkshopUrl.Should().Be(WorkshopUrl);
        _viewModel.Author.Should().Be("Ada Lovelace");
        _editorSettings.WorkshopUrl.Should().Be(WorkshopUrl);
        _editorSettings.WorkshopAuthor.Should().Be("Ada Lovelace");
    }

    [Test]
    public async Task Remove_Cancelled_KeepsStoredKey()
    {
        await SeedStoredConnectionAsync();
        await _viewModel.InitializeAsync();
        SetRemoveConfirmationResult(confirmed: false);

        await _viewModel.RemoveWorkshopKeyCommand.ExecuteAsync(null);

        _viewModel.IsStoredKeyVisible.Should().BeTrue();

        var keyResult = await _credentialService.GetWorkshopKeyAsync();
        keyResult.IsSuccess.Should().BeTrue();
        keyResult.Value.Should().Be(TestWorkshopKey);
    }
}
