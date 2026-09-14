using Celbridge.DocumentEditors;
using Celbridge.Settings;

namespace Celbridge.Tests.DocumentEditors;

/// <summary>
/// Tests that DocumentEditorsBundledPackageProvider registers the Notes editor package only while the
/// note-editor feature flag is on, and registers the other bundled editors either way.
/// </summary>
[TestFixture]
public class DocumentEditorsBundledPackageProviderTests
{
    private const string NotesFolderName = "Notes";

    [Test]
    public void GetBundledPackages_NoteEditorOff_LeavesOutTheNotesPackage()
    {
        var folderNames = GetPackageFolderNames(noteEditorEnabled: false);

        folderNames.Should().NotContain(NotesFolderName);
    }

    [Test]
    public void GetBundledPackages_NoteEditorOn_IncludesTheNotesPackage()
    {
        var folderNames = GetPackageFolderNames(noteEditorEnabled: true);

        folderNames.Should().Contain(NotesFolderName);
    }

    [Test]
    public void GetBundledPackages_NoteEditorFlag_LeavesTheOtherPackagesUnchanged()
    {
        var withNotes = GetPackageFolderNames(noteEditorEnabled: true);
        var withoutNotes = GetPackageFolderNames(noteEditorEnabled: false);

        withoutNotes.Should().NotBeEmpty();
        withNotes.Where(name => name != NotesFolderName).Should().Equal(withoutNotes);
    }

    private static IReadOnlyList<string> GetPackageFolderNames(bool noteEditorEnabled)
    {
        var featureFlags = Substitute.For<IFeatureFlags>();
        featureFlags.IsEnabled(FeatureFlagConstants.NoteEditor).Returns(noteEditorEnabled);

        var provider = new DocumentEditorsBundledPackageProvider(featureFlags);

        return provider.GetBundledPackages()
            .Select(descriptor => Path.GetFileName(descriptor.Folder))
            .ToList();
    }
}
