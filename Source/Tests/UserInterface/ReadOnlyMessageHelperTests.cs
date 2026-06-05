using Celbridge.Resources;
using Celbridge.UserInterface.Helpers;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests for the state-to-resw-key mapping that drives every read-only dimming
/// surface (explorer tree, file picker, future breadcrumb). Pinning the key per
/// state catches a renamed resw entry before it breaks tooltips in production.
/// </summary>
[TestFixture]
public class ReadOnlyMessageHelperTests
{
    private IStringLocalizer _localizer = null!;

    [SetUp]
    public void Setup()
    {
        _localizer = Substitute.For<IStringLocalizer>();
        // IStringLocalizer.GetString(string) is an extension method that calls
        // the indexer at runtime, so the indexer is what NSubstitute can stub.
        _localizer[Arg.Any<string>()].Returns(callInfo =>
        {
            // Echo "localized:<key>" so each test can assert on which key was
            // looked up without having to wire a full resw stub.
            var key = (string)callInfo[0];
            return new LocalizedString(key, $"localized:{key}");
        });
    }

    [Test]
    public void Writable_ReturnsNull_SoTooltipBindingCollapses()
    {
        var message = ReadOnlyMessageHelper.GetReadOnlyMessage(WritableState.Writable, _localizer);

        message.Should().BeNull();
    }

    [Test]
    public void Locked_ResolvesResource_ReadOnly_Locked_Key()
    {
        var message = ReadOnlyMessageHelper.GetReadOnlyMessage(WritableState.Locked, _localizer);

        message.Should().Be("localized:Resource_ReadOnly_Locked");
    }

    [Test]
    public void ReadOnlyAttribute_ResolvesResource_ReadOnly_ReadOnlyAttribute_Key()
    {
        var message = ReadOnlyMessageHelper.GetReadOnlyMessage(WritableState.ReadOnlyAttribute, _localizer);

        message.Should().Be("localized:Resource_ReadOnly_ReadOnlyAttribute");
    }

    [Test]
    public void ReadOnlyRoot_ResolvesResource_ReadOnly_ReadOnlyRoot_Key()
    {
        var message = ReadOnlyMessageHelper.GetReadOnlyMessage(WritableState.ReadOnlyRoot, _localizer);

        message.Should().Be("localized:Resource_ReadOnly_ReadOnlyRoot");
    }
}
