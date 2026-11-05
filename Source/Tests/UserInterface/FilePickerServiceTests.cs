using Celbridge.Dialog;
using Celbridge.Platform;
using Celbridge.UserInterface.Services;
using Celbridge.Utilities.Platform;
using Windows.Storage.Pickers;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers the start location each picker is given, and the file types of a save dialog. A save dialog that requires a
/// start location gets Downloads in place of the folder last used. A name with no extension gets the platform's file
/// type for any name.
/// </summary>
[TestFixture]
public class FilePickerServiceTests
{
    [TestCase(PickerStartFolder.Documents, PickerLocationId.DocumentsLibrary)]
    [TestCase(PickerStartFolder.Downloads, PickerLocationId.Downloads)]
    [TestCase(PickerStartFolder.LastUsed, PickerLocationId.Unspecified)]
    public void TheStartLocation_FollowsTheStartFolder(PickerStartFolder startFolder, PickerLocationId expected)
    {
        var service = CreateService(requiresStartLocation: false);

        service.GetPickerLocationId(startFolder).Should().Be(expected);
    }

    [TestCase(PickerStartFolder.Documents, PickerLocationId.DocumentsLibrary)]
    [TestCase(PickerStartFolder.Downloads, PickerLocationId.Downloads)]
    [TestCase(PickerStartFolder.LastUsed, PickerLocationId.Downloads)]
    public void TheStartLocation_IsNeverUnspecified_WhenTheSaveDialogRequiresOne(
        PickerStartFolder startFolder,
        PickerLocationId expected)
    {
        var service = CreateService(requiresStartLocation: true);

        service.GetPickerLocationId(startFolder).Should().Be(expected);
    }

    [Test]
    public void TheFileTypes_AreTheExtensions_WhenThereAreAny()
    {
        var service = CreateService(anyNameFileType: ".public.data");

        service.GetFileTypes([".txt"]).Should().Equal(".txt");
    }

    [TestCase(".")]
    [TestCase(".public.data")]
    public void TheFileTypes_AreThePlatformsFileTypeForAnyName_WhenThereAreNoExtensions(string anyNameFileType)
    {
        var service = CreateService(anyNameFileType: anyNameFileType);

        service.GetFileTypes([]).Should().Equal(anyNameFileType);
    }

    [Test]
    public void TheFileTypes_AreEmpty_WhenThePlatformHasNoFileTypeForAnyName()
    {
        var service = CreateService(anyNameFileType: null);

        service.GetFileTypes([]).Should().BeEmpty();
    }

    [Test]
    public void ThePlatformsFileTypeForAnyName_IsOneThePickerAccepts()
    {
        var fileType = new PlatformInfo().SaveDialogAnyNameFileType;
        if (fileType is null)
        {
            return;
        }

        var picker = new FileSavePicker();
        var add = () => picker.FileTypeChoices.Add("File", [fileType]);

        add.Should().NotThrow();
    }

    private static FilePickerService CreateService(bool requiresStartLocation = false, string? anyNameFileType = null)
    {
        var platformInfo = Substitute.For<IPlatformInfo>();
        platformInfo.SaveDialogRequiresStartLocation.Returns(requiresStartLocation);
        platformInfo.SaveDialogAnyNameFileType.Returns(anyNameFileType);

        return new FilePickerService(Substitute.For<ILocalFileSystem>(), platformInfo);
    }
}
