using Celbridge.Settings;
using Celbridge.Settings.Services;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Settings;
using Celbridge.UserInterface;
using Celbridge.UserInterface.ViewModels.Controls;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the SecretField view model. The secret round-trips through the real SettingsService over
/// an in-memory credential store fake. The tests run the view model's commands directly. The field's
/// buttons run the same commands.
/// </summary>
[TestFixture]
public class SecretFieldViewModelTests
{
    private const string TestSecretValue = "abc123_supersecretvalue";

    private static readonly SettingDescriptor<string> TestSecret =
        new("Test.Secret", SettingScope.Protected, "");

    private FakeSettingsStore _settingsStore = null!;
    private FakeCredentialStore _credentialStore = null!;
    private SettingsService _settingsService = null!;
    private SecretFieldViewModel _viewModel = null!;

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

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(
            callInfo => new LocalizedString(callInfo.Arg<string>(), callInfo.Arg<string>()));

        _viewModel = new SecretFieldViewModel(
            Substitute.For<ILogger<SecretFieldViewModel>>(),
            _settingsService,
            stringLocalizer);
    }

    // Opens the entry field and types the given secret into it.
    private void EnterSecret(string secret)
    {
        _viewModel.BeginChangeCommand.Execute(null);
        _viewModel.SecretInput = secret;
    }

    private bool IsSecretStored()
    {
        return _settingsService.IsConfigured(TestSecret);
    }

    private string GetStoredSecret()
    {
        var result = _settingsService.TryGet(TestSecret);
        result.IsSuccess.Should().BeTrue();

        return result.Value;
    }

    [Test]
    public void Initialize_NothingStored_ShowsSet()
    {
        _viewModel.Initialize(TestSecret);

        _viewModel.IsStoreAvailable.Should().BeTrue();
        _viewModel.IsSetVisible.Should().BeTrue();
        _viewModel.IsStoredVisible.Should().BeFalse();
    }

    [Test]
    public void Initialize_StoredSecret_ShowsMask()
    {
        _settingsService.Set(TestSecret, TestSecretValue);

        _viewModel.Initialize(TestSecret);

        _viewModel.IsStoredVisible.Should().BeTrue();
        _viewModel.IsSetVisible.Should().BeFalse();
        _viewModel.StoredSecretDisplay.Should().Be("********");
    }

    [Test]
    public void Initialize_StoreUnavailable_ShowsOnlyTheNotice()
    {
        _credentialStore.Available = false;

        _viewModel.Initialize(TestSecret);

        _viewModel.IsStoreAvailable.Should().BeFalse();
        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        _viewModel.StatusMessage.Should().Be("SecretField_StoreUnavailable");
        _viewModel.IsSetVisible.Should().BeFalse();
        _viewModel.IsStoredVisible.Should().BeFalse();
    }

    [Test]
    public void Initialize_SettingWithAnotherScope_Throws()
    {
        var applicationSetting = new SettingDescriptor<string>("Test.Plain", SettingScope.Application, "");

        Action act = () => _viewModel.Initialize(applicationSetting);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Save_NewSecret_StoresSecretAndShowsMask()
    {
        _viewModel.Initialize(TestSecret);
        EnterSecret(TestSecretValue);

        _viewModel.SaveCommand.Execute(null);

        _viewModel.IsEditVisible.Should().BeFalse();
        _viewModel.IsStoredVisible.Should().BeTrue();
        _viewModel.IsStatusVisible.Should().BeFalse();
        GetStoredSecret().Should().Be(TestSecretValue);
    }

    [Test]
    public void Change_Canceled_LeavesStoredSecretUntouched()
    {
        _settingsService.Set(TestSecret, TestSecretValue);
        _viewModel.Initialize(TestSecret);
        EnterSecret("a_replacement_secret");

        _viewModel.CancelChangeCommand.Execute(null);

        _viewModel.IsEditVisible.Should().BeFalse();
        _viewModel.IsStoredVisible.Should().BeTrue();
        GetStoredSecret().Should().Be(TestSecretValue);
    }

    [Test]
    public void Change_CanceledAfterFailedSave_ClearsTheError()
    {
        _viewModel.Initialize(TestSecret);
        EnterSecret(TestSecretValue);
        _credentialStore.FailStore = true;
        _viewModel.SaveCommand.Execute(null);

        _viewModel.CancelChangeCommand.Execute(null);

        _viewModel.IsStatusVisible.Should().BeFalse();
        _viewModel.IsSetVisible.Should().BeTrue();
    }

    [Test]
    public void Save_BlankEntry_CannotBeSaved()
    {
        _viewModel.Initialize(TestSecret);
        EnterSecret("   ");

        _viewModel.IsSaveEnabled.Should().BeFalse();

        _viewModel.SaveCommand.Execute(null);

        IsSecretStored().Should().BeFalse();
    }

    [Test]
    public void Save_StoreFails_ShowsErrorAndKeepsTheEntry()
    {
        _viewModel.Initialize(TestSecret);
        EnterSecret(TestSecretValue);
        _credentialStore.FailStore = true;

        _viewModel.SaveCommand.Execute(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        _viewModel.StatusMessage.Should().Be("SecretField_SaveFailed");
        _viewModel.IsEditVisible.Should().BeTrue();
        _viewModel.SecretInput.Should().Be(TestSecretValue);
        IsSecretStored().Should().BeFalse();
    }

    [Test]
    public void Remove_Confirmed_RemovesStoredSecret()
    {
        _settingsService.Set(TestSecret, TestSecretValue);
        _viewModel.Initialize(TestSecret);

        _viewModel.BeginRemoveCommand.Execute(null);
        _viewModel.IsRemoveConfirmVisible.Should().BeTrue();

        _viewModel.ConfirmRemoveCommand.Execute(null);

        _viewModel.IsRemoveConfirmVisible.Should().BeFalse();
        _viewModel.IsSetVisible.Should().BeTrue();
        _viewModel.IsStoredVisible.Should().BeFalse();
        IsSecretStored().Should().BeFalse();
    }

    [Test]
    public void Remove_DeleteFails_ShowsErrorAndKeepsMask()
    {
        _settingsService.Set(TestSecret, TestSecretValue);
        _viewModel.Initialize(TestSecret);
        _credentialStore.FailDelete = true;
        _viewModel.BeginRemoveCommand.Execute(null);

        _viewModel.ConfirmRemoveCommand.Execute(null);

        _viewModel.IsStatusVisible.Should().BeTrue();
        _viewModel.StatusSeverity.Should().Be(StatusSeverity.Error);
        _viewModel.StatusMessage.Should().Be("SecretField_RemoveFailed");
        _viewModel.IsStoredVisible.Should().BeTrue();
        _viewModel.IsSetVisible.Should().BeFalse();
        GetStoredSecret().Should().Be(TestSecretValue);
    }

    [Test]
    public void Remove_Canceled_KeepsStoredSecret()
    {
        _settingsService.Set(TestSecret, TestSecretValue);
        _viewModel.Initialize(TestSecret);
        _viewModel.BeginRemoveCommand.Execute(null);

        _viewModel.CancelRemoveCommand.Execute(null);

        _viewModel.IsRemoveConfirmVisible.Should().BeFalse();
        _viewModel.IsStoredVisible.Should().BeTrue();
        GetStoredSecret().Should().Be(TestSecretValue);
    }
}
