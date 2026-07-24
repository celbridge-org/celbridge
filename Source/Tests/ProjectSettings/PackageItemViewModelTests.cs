using Celbridge.Packages;
using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.Tests.ProjectSettings;

[TestFixture]
public class PackageItemViewModelTests
{
    private static PackageItemViewModel CreatePackage()
    {
        var info = new PackageItemInfo
        {
            Name = "test-package",
            DisplayName = "Test Package"
        };

        return new PackageItemViewModel(info, isEnabled: true, (name, disabled) => { }, resource => { }, resource => { });
    }

    private static ContributionItemViewModel CreateContribution(params ContributionIssue[] issues)
    {
        var info = new ContributionItemInfo
        {
            PackageName = "test-package",
            ContributionId = "editor",
            DisplayName = "Test Editor",
            Issues = issues
        };

        return new ContributionItemViewModel(info, (contribution, enabled) => { }, resource => { }, resource => { });
    }

    /// <summary>
    /// The package header carries the warning badge for issues that belong to its contributions, which sit
    /// inside the expander and are not visible until it is opened.
    /// </summary>
    [Test]
    public void HasIssues_IsTrueWhenAnyContributionHasIssues()
    {
        var package = CreatePackage();
        package.Contributions.Add(CreateContribution());

        package.HasIssues.Should().BeFalse();

        var issue = new ContributionIssue
        {
            EditorId = "test-package.broken",
            Kind = ContributionIssueKind.UnresolvedIcon,
            Value = "no-such-icon"
        };
        package.Contributions.Add(CreateContribution(issue));

        package.HasIssues.Should().BeTrue();
    }
}
