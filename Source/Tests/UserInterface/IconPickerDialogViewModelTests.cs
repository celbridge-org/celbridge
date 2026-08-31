using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// The icon picker's search box, exercised over the real bundled icon set. The filter is what makes two
/// thousand icons browsable, and the keywords are what reach an icon whose Bootstrap name is not the word
/// the user would think of.
/// </summary>
[TestFixture]
public class IconPickerDialogViewModelTests
{
    private static IconPickerDialogViewModel CreateViewModel(string selectedIconName = "")
    {
        var viewModel = new IconPickerDialogViewModel(new IconService());
        viewModel.Initialize(selectedIconName);

        return viewModel;
    }

    [Test]
    public void Initialize_WithNoSearchTerm_ListsEverySupportedIcon()
    {
        var supportedIcons = new IconService().GetSupportedIcons();

        var viewModel = CreateViewModel();

        viewModel.FilteredItems.Should().HaveCount(supportedIcons.Count);
        viewModel.IsEmptyMessageVisible.Should().BeFalse();
    }

    [Test]
    public void Initialize_WithASupportedIconNamed_OpensOnThatIcon()
    {
        var viewModel = CreateViewModel("bs-journal-text");

        viewModel.SelectedItem.Should().NotBeNull();
        viewModel.SelectedItem!.IconName.Should().Be("bs-journal-text");
        viewModel.IsSubmitEnabled.Should().BeTrue();
    }

    /// <summary>
    /// A field can hold a name the picker does not offer, either a typing mistake or a Nerd Fonts name
    /// that resolves natively. The dialog opens on the full list rather than on nothing.
    /// </summary>
    [TestCase("bs-not-a-real-icon", Description = "unknown name")]
    [TestCase("nf-seti-json", Description = "resolvable but not offered")]
    [TestCase("", Description = "no icon named")]
    public void Initialize_WithAnUnsupportedIconNamed_OpensWithNoSelection(string iconName)
    {
        var viewModel = CreateViewModel(iconName);

        viewModel.SelectedItem.Should().BeNull();
        viewModel.IsSubmitEnabled.Should().BeFalse();
    }

    [TestCase("journal-text")]
    [TestCase("JOURNAL-TEXT")]
    [TestCase("  journal-text  ")]
    public void SearchText_MatchesTheNameOnASubstring(string searchText)
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = searchText;

        viewModel.FilteredItems.Should().Contain(item => item.IconName == "bs-journal-text");
    }

    /// <summary>
    /// The words the user reaches for are rarely Bootstrap's names: saving is "floppy", settings is
    /// "gear", a user is "person".
    /// </summary>
    [TestCase("save", "bs-floppy")]
    [TestCase("settings", "bs-gear")]
    [TestCase("user", "bs-person")]
    public void SearchText_MatchesAKeywordTheNameDoesNotCarry(string searchText, string expectedIconName)
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = searchText;

        viewModel.FilteredItems.Should().Contain(item => item.IconName == expectedIconName);
    }

    /// <summary>
    /// An icon named for the term outranks one that merely carries it as a keyword, so a keyword match
    /// that sorts earlier alphabetically ("bs-bookmark" is tagged "save") cannot take the top of the list.
    /// </summary>
    [Test]
    public void SearchText_ListsNameMatchesBeforeKeywordMatches()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = "save";

        var iconNames = viewModel.FilteredItems.Select(item => item.IconName).ToList();
        iconNames[0].Should().Be("bs-save");
        iconNames.IndexOf("bs-floppy").Should().BeGreaterThan(iconNames.IndexOf("bs-save2"));
        iconNames.IndexOf("bs-bookmark").Should().BeGreaterThan(iconNames.IndexOf("bs-save2"));
    }

    [Test]
    public void SearchText_MatchingNothing_ReportsAnEmptyList()
    {
        var viewModel = CreateViewModel();

        viewModel.SearchText = "there-is-no-icon-for-this";

        viewModel.FilteredItems.Should().BeEmpty();
        viewModel.IsEmptyMessageVisible.Should().BeTrue();
    }

    [Test]
    public void SearchText_Cleared_ListsEverySupportedIconAgain()
    {
        var supportedIcons = new IconService().GetSupportedIcons();
        var viewModel = CreateViewModel();

        viewModel.SearchText = "journal";
        viewModel.SearchText = string.Empty;

        viewModel.FilteredItems.Should().HaveCount(supportedIcons.Count);
        viewModel.IsEmptyMessageVisible.Should().BeFalse();
    }
}
