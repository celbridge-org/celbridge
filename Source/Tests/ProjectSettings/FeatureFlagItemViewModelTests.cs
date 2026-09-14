using Celbridge.ProjectSettings.ViewModels;

namespace Celbridge.Tests.ProjectSettings;

/// <summary>
/// Covers how a feature flag toggle records its value: a flag switched to its default leaves no entry in the
/// project's features table, one switched away from its default writes the value, and a reset shows the
/// default without writing anything more.
/// </summary>
[TestFixture]
public class FeatureFlagItemViewModelTests
{
    private readonly List<(string FlagName, bool? Value)> _writes = new();

    [SetUp]
    public void SetUp()
    {
        _writes.Clear();
    }

    private FeatureFlagItemViewModel CreateFlag(bool applicationValue, bool? projectValue = null)
    {
        var info = new FeatureFlagItemInfo
        {
            FlagName = "sample-flag",
            Title = "Sample",
            Description = "A sample flag.",
            ApplicationValue = applicationValue,
            ProjectValue = projectValue,
        };

        return new FeatureFlagItemViewModel(info, (flagName, value) => _writes.Add((flagName, value)));
    }

    [Test]
    public void IsOn_WithNoProjectValue_FollowsTheDefault()
    {
        var defaultOn = CreateFlag(applicationValue: true);
        var defaultOff = CreateFlag(applicationValue: false);

        defaultOn.IsOn.Should().BeTrue();
        defaultOn.HasProjectValue.Should().BeFalse();
        defaultOff.IsOn.Should().BeFalse();
        defaultOff.HasProjectValue.Should().BeFalse();
    }

    [Test]
    public void IsOn_WithAProjectValue_FollowsTheProject()
    {
        var flag = CreateFlag(applicationValue: false, projectValue: true);

        flag.IsOn.Should().BeTrue();
        flag.HasProjectValue.Should().BeTrue();
    }

    [Test]
    public void HasProjectValue_ForAnEntryMatchingTheDefault_IsTrue()
    {
        // An entry that pins a flag to its default still sits in the file, so a reset has it to clear.
        var flag = CreateFlag(applicationValue: true, projectValue: true);

        flag.IsOn.Should().BeTrue();
        flag.HasProjectValue.Should().BeTrue();
    }

    [Test]
    public void Loading_WritesNothing()
    {
        CreateFlag(applicationValue: false, projectValue: true);
        CreateFlag(applicationValue: true);

        _writes.Should().BeEmpty("opening the section must not rewrite the project's features table");
    }

    [Test]
    public void SwitchingAwayFromTheDefault_WritesTheValue()
    {
        var flag = CreateFlag(applicationValue: false);

        flag.IsOn = true;

        _writes.Should().ContainSingle().Which.Should().Be(("sample-flag", (bool?)true));
        flag.HasProjectValue.Should().BeTrue();
    }

    [Test]
    public void SwitchingOffADefaultOnFlag_WritesOff()
    {
        var flag = CreateFlag(applicationValue: true);

        flag.IsOn = false;

        _writes.Should().ContainSingle().Which.Should().Be(("sample-flag", (bool?)false));
    }

    [Test]
    public void SwitchingBackToTheDefault_ClearsTheEntry()
    {
        var flag = CreateFlag(applicationValue: false, projectValue: true);

        flag.IsOn = false;

        _writes.Should().ContainSingle().Which.Should().Be(("sample-flag", (bool?)null));
        flag.HasProjectValue.Should().BeFalse();
    }

    [Test]
    public void ShowDefault_ReturnsToTheDefaultWithoutWriting()
    {
        var flag = CreateFlag(applicationValue: false, projectValue: true);

        flag.ShowDefault();

        flag.IsOn.Should().BeFalse();
        flag.HasProjectValue.Should().BeFalse();
        _writes.Should().BeEmpty("the reset has already cleared the project's entries");
    }

    [Test]
    public void ShowDefault_ThenSwitching_WritesAgain()
    {
        var flag = CreateFlag(applicationValue: false, projectValue: true);
        flag.ShowDefault();

        flag.IsOn = true;

        _writes.Should().ContainSingle().Which.Should().Be(("sample-flag", (bool?)true));
    }
}
