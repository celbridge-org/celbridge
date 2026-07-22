using Celbridge.Packages;
using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers the "name the one, count the many" rule for issue messages. The rule is tested through the
/// message choice rather than the rendered string, because looking a string up needs a localizer that
/// the test host does not provide.
/// </summary>
[TestFixture]
public class ProjectSettingsLabelsTests
{
    private static ContributionIssue UnresolvedIcon(string value)
    {
        return new ContributionIssue
        {
            EditorId = "test-package.editor",
            Kind = ContributionIssueKind.UnresolvedIcon,
            Value = value
        };
    }

    [Test]
    public void ContributionIssueMessage_OneIssue_NamesTheValueThatFailed()
    {
        var message = ProjectSettingsLabels.ContributionIssueMessage([UnresolvedIcon("bs-no-such-icon")]);

        message.ResourceKey.Should().Be("ProjectSettings_ContributionIssue_UnresolvedIcon_Single");
        message.Argument.Should().Be("bs-no-such-icon");
    }

    [Test]
    public void ContributionIssueMessage_SeveralIssues_ReportsTheCount()
    {
        var message = ProjectSettingsLabels.ContributionIssueMessage(
            [UnresolvedIcon("bs-no-such-icon"), UnresolvedIcon("nf-no-such-icon"), UnresolvedIcon("also-missing")]);

        message.ResourceKey.Should().Be("ProjectSettings_ContributionIssue_Multiple");
        message.Argument.Should().Be("3");
    }

    /// <summary>
    /// Naming a single issue depends on its kind, so a kind with no message of its own has to fall back
    /// to the count form rather than render nothing.
    /// </summary>
    [Test]
    public void ContributionIssueMessage_OneIssueOfAnUnhandledKind_FallsBackToTheCount()
    {
        var unhandled = new ContributionIssue
        {
            EditorId = "test-package.editor",
            Kind = (ContributionIssueKind)999,
            Value = "something"
        };

        var message = ProjectSettingsLabels.ContributionIssueMessage([unhandled]);

        message.ResourceKey.Should().Be("ProjectSettings_ContributionIssue_Multiple");
        message.Argument.Should().Be("1");
    }

    [Test]
    public void PackageIssueMessage_OneContribution_NamesIt()
    {
        var message = ProjectSettingsLabels.PackageIssueMessage(["Fury Editor"]);

        message.ResourceKey.Should().Be("ProjectSettings_PackageIssue_Single");
        message.Argument.Should().Be("Fury Editor");
    }

    [Test]
    public void PackageIssueMessage_SeveralContributions_ReportsTheCount()
    {
        var message = ProjectSettingsLabels.PackageIssueMessage(["Fury Editor", "Story Editor"]);

        message.ResourceKey.Should().Be("ProjectSettings_PackageIssue_Multiple");
        message.Argument.Should().Be("2");
    }

    /// <summary>
    /// Every key the chooser can return must exist in the resource file, or the message renders as the
    /// raw key at runtime.
    /// </summary>
    [Test]
    public void EveryChosenResourceKeyExistsInTheResourceFile()
    {
        var keys = new[]
        {
            ProjectSettingsLabels.ContributionIssueMessage([UnresolvedIcon("x")]).ResourceKey,
            ProjectSettingsLabels.ContributionIssueMessage([UnresolvedIcon("x"), UnresolvedIcon("y")]).ResourceKey,
            ProjectSettingsLabels.PackageIssueMessage(["one"]).ResourceKey,
            ProjectSettingsLabels.PackageIssueMessage(["one", "two"]).ResourceKey
        };

        var resourceText = File.ReadAllText(ResourceFilePath());
        foreach (var key in keys)
        {
            resourceText.Should().Contain($"\"{key}\"", $"the resource file must define '{key}'");
        }
    }

    private static string ResourceFilePath()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null
            && !Directory.Exists(Path.Combine(directory.FullName, "Source")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "Could not locate the repository root from the test directory.");

        return Path.Combine(directory!.FullName, "Source", "Celbridge", "Resources", "Strings", "en-US", "Resources.resw");
    }
}
