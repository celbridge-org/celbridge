using Celbridge.Dialog;
using Celbridge.Platform;
using Celbridge.UserInterface.Services;
using Windows.Storage.Pickers;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Covers the start location each picker is given. A save dialog that requires a start location gets Downloads in
/// place of the folder last used.
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

    private static FilePickerService CreateService(bool requiresStartLocation)
    {
        var platformInfo = Substitute.For<IPlatformInfo>();
        platformInfo.SaveDialogRequiresStartLocation.Returns(requiresStartLocation);

        return new FilePickerService(Substitute.For<ILocalFileSystem>(), platformInfo);
    }
}
