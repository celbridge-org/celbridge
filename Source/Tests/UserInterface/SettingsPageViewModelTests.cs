using Celbridge.Credentials;
using Celbridge.Settings.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Settings;
using Celbridge.UserInterface.ViewModels.Pages;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the Workshop connection section of the Settings page view
/// model, driven against the real CredentialService over fakes.
/// </summary>
[TestFixture]
public class SettingsPageViewModelTests
{
    private const string CredentialsFilePath = @"C:\AppData\Celbridge\credentials.json";
    private const string WorkshopUrl = "https://workshop.celbridge.org";
    private const string ApplicationKey = "kpf_abc123_supersecretvalue";

    private FakeFileSystem _fileSystem = null!;
    private FakeCredentialProtector _protector = null!;
    private CredentialService _credentialService = null!;
    private SettingsPageViewModel _viewModel = null!;

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

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new SettingsPageViewModel(
            Substitute.For<ILogger<SettingsPageViewModel>>(),
            _credentialService,
            stringLocalizer);
    }

    [Test]
    public async Task Initialize_NothingStored_ShowsKeyEntry()
    {
        await _viewModel.InitializeAsync();

        _viewModel.IsStoreAvailable.Should().BeTrue();
        _viewModel.IsKeyEntryVisible.Should().BeTrue();
        _viewModel.IsStoredKeyVisible.Should().BeFalse();
        _viewModel.IsClearVisible.Should().BeFalse();
    }

    [Test]
    public async Task Initialize_StoreUnavailable_ShowsErrorAndDisablesEntry()
    {
        _protector.Available = false;

        await _viewModel.InitializeAsync();

        _viewModel.IsStoreAvailable.Should().BeFalse();
        _viewModel.IsErrorVisible.Should().BeTrue();
        _viewModel.IsKeyEntryVisible.Should().BeFalse();
    }

    [Test]
    public async Task Initialize_StoredConnection_ShowsUrlAndKeyPrefix()
    {
        await _credentialService.SetWorkshopConnectionAsync(new WorkshopConnection(WorkshopUrl, ApplicationKey));

        await _viewModel.InitializeAsync();

        _viewModel.WorkshopUrl.Should().Be(WorkshopUrl);
        _viewModel.StoredKeyDisplay.Should().Be("kpf_abc123_...");
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        _viewModel.IsKeyEntryVisible.Should().BeFalse();
        _viewModel.IsClearVisible.Should().BeTrue();
    }

    [Test]
    public async Task Save_NewConnection_StoresAndShowsStoredKey()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.ApplicationKey = ApplicationKey;

        await _viewModel.SaveWorkshopConnectionCommand.ExecuteAsync(null);

        _viewModel.IsErrorVisible.Should().BeFalse();
        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.ApplicationKey.Should().BeEmpty("the entered key should not linger in the view model");
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
        _viewModel.StoredKeyDisplay.Should().Be("kpf_abc123_...");

        var getResult = await _credentialService.GetWorkshopConnectionAsync();
        getResult.IsSuccess.Should().BeTrue();

        var storedConnection = getResult.Value;
        storedConnection.Should().Be(new WorkshopConnection(WorkshopUrl, ApplicationKey));
    }

    [Test]
    public async Task Save_NewConnection_StoresAuthor()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.ApplicationKey = ApplicationKey;
        _viewModel.Author = "Ada Lovelace";

        await _viewModel.SaveWorkshopConnectionCommand.ExecuteAsync(null);

        _viewModel.IsErrorVisible.Should().BeFalse();

        var getResult = await _credentialService.GetWorkshopConnectionAsync();
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Author.Should().Be("Ada Lovelace");
    }

    [Test]
    public async Task Initialize_StoredConnection_PopulatesAuthor()
    {
        await _credentialService.SetWorkshopConnectionAsync(
            new WorkshopConnection(WorkshopUrl, ApplicationKey, "Ada Lovelace"));

        await _viewModel.InitializeAsync();

        _viewModel.Author.Should().Be("Ada Lovelace");
    }

    [Test]
    public async Task Save_InvalidUrl_ShowsErrorAndStoresNothing()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = "http://workshop.celbridge.org";
        _viewModel.ApplicationKey = ApplicationKey;

        await _viewModel.SaveWorkshopConnectionCommand.ExecuteAsync(null);

        _viewModel.IsErrorVisible.Should().BeTrue();
        _fileSystem.Files.Should().NotContainKey(CredentialsFilePath);
    }

    [Test]
    public async Task Save_EmptyKey_ShowsError()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.ApplicationKey = string.Empty;

        await _viewModel.SaveWorkshopConnectionCommand.ExecuteAsync(null);

        _viewModel.IsErrorVisible.Should().BeTrue();
        _fileSystem.Files.Should().NotContainKey(CredentialsFilePath);
    }

    [Test]
    public async Task Save_KeyWithoutExpectedPrefix_WarnsButStores()
    {
        await _viewModel.InitializeAsync();
        _viewModel.WorkshopUrl = WorkshopUrl;
        _viewModel.ApplicationKey = "not-a-kpf-shaped-key";

        await _viewModel.SaveWorkshopConnectionCommand.ExecuteAsync(null);

        _viewModel.IsErrorVisible.Should().BeFalse();
        _viewModel.IsWarningVisible.Should().BeTrue("the prefix check is a typo guard, not a gate");

        var getResult = await _credentialService.GetWorkshopConnectionAsync();
        getResult.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task Save_UrlOnlyUpdate_PreservesStoredKey()
    {
        await _credentialService.SetWorkshopConnectionAsync(new WorkshopConnection(WorkshopUrl, ApplicationKey));
        await _viewModel.InitializeAsync();

        var updatedUrl = "https://other.celbridge.org";
        _viewModel.WorkshopUrl = updatedUrl;

        await _viewModel.SaveWorkshopConnectionCommand.ExecuteAsync(null);

        _viewModel.IsErrorVisible.Should().BeFalse();

        var getResult = await _credentialService.GetWorkshopConnectionAsync();
        getResult.IsSuccess.Should().BeTrue();

        var storedConnection = getResult.Value;
        storedConnection.Should().Be(new WorkshopConnection(updatedUrl, ApplicationKey));
    }

    [Test]
    public async Task Clear_RemovesConnectionAndResetsForm()
    {
        await _credentialService.SetWorkshopConnectionAsync(new WorkshopConnection(WorkshopUrl, ApplicationKey));
        await _viewModel.InitializeAsync();

        await _viewModel.ClearWorkshopConnectionCommand.ExecuteAsync(null);

        _viewModel.WorkshopUrl.Should().BeEmpty();
        _viewModel.StoredKeyDisplay.Should().BeEmpty();
        _viewModel.IsKeyEntryVisible.Should().BeTrue();
        _viewModel.IsClearVisible.Should().BeFalse();
        _fileSystem.Files.Should().NotContainKey(CredentialsFilePath);
    }

    [Test]
    public async Task ReplaceThenCancel_RestoresStoredKeyDisplay()
    {
        await _credentialService.SetWorkshopConnectionAsync(new WorkshopConnection(WorkshopUrl, ApplicationKey));
        await _viewModel.InitializeAsync();

        _viewModel.ReplaceApplicationKeyCommand.Execute(null);

        _viewModel.IsKeyEntryVisible.Should().BeTrue();
        _viewModel.IsStoredKeyVisible.Should().BeFalse();
        _viewModel.IsCancelReplaceVisible.Should().BeTrue();

        _viewModel.ApplicationKey = "kpf_new123_partiallyentered";
        _viewModel.CancelReplaceApplicationKeyCommand.Execute(null);

        _viewModel.ApplicationKey.Should().BeEmpty();
        _viewModel.IsKeyEntryVisible.Should().BeFalse();
        _viewModel.IsStoredKeyVisible.Should().BeTrue();
    }
}
