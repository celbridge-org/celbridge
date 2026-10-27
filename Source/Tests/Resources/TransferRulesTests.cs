using Celbridge.Resources;
using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests the rule that the destination of a move or copy lies outside the source.
/// </summary>
[TestFixture]
public class TransferRulesTests
{
    [Test]
    public void ADestinationInsideTheSourceIsRefused()
    {
        TransferRules.CheckDestination(new ResourceKey("a"), new ResourceKey("a/a")).IsFailure.Should().BeTrue();
        TransferRules.CheckDestination(new ResourceKey("a"), new ResourceKey("a/b/a")).IsFailure.Should().BeTrue();
    }

    [Test]
    public void ADestinationOutsideTheSourceIsAccepted()
    {
        TransferRules.CheckDestination(new ResourceKey("a"), new ResourceKey("b/a")).IsSuccess.Should().BeTrue();
        TransferRules.CheckDestination(new ResourceKey("a"), new ResourceKey("ab/a")).IsSuccess.Should().BeTrue();
        TransferRules.CheckDestination(new ResourceKey("a/x.txt"), new ResourceKey("x.txt")).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void TheSourceItselfIsAccepted()
    {
        TransferRules.CheckDestination(new ResourceKey("a"), new ResourceKey("a")).IsSuccess.Should().BeTrue();
    }

    [Test]
    public void ADestinationOnAnotherRootIsAccepted()
    {
        TransferRules.CheckDestination(new ResourceKey("a"), new ResourceKey("temp:a/a")).IsSuccess.Should().BeTrue();
    }
}
